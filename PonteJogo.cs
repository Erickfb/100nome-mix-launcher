using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

// A Steam e o CS2 podem usar portas de origem diferentes. Cada uma conserva a
// própria sessão até o dedicado para preservar challenges, reservas e respostas.
internal sealed class PonteJogo
{
    private readonly UdpClient local;
    private readonly ulong idServidor;
    internal readonly bool UsaUdp;
    private long solicitadaEm = Stopwatch.GetTimestamp();
    internal readonly IPEndPoint Endereco;
    internal readonly Task Termino;

    private PonteJogo(ulong id, bool udp, CanalJogo inicial)
    {
        idServidor = id;
        UsaUdp = udp;
        local = AbrirPorta();
        Endereco = (IPEndPoint)local.Client.LocalEndPoint;
        Termino = AtenderAsync(inicial);
    }

    internal static async Task<PonteJogo> AbrirAsync(ulong id, bool udp)
    {
        var canal = await CanalJogo.AbrirValidadoAsync(id, udp, UsoEntrada.Jogar, CancellationToken.None).ConfigureAwait(false);
        try { return new PonteJogo(id, udp, canal); }
        catch { canal.Dispose(); throw; }
    }

    internal void Parar() { local.Close(); }
    internal void AguardarJogo() { Interlocked.Exchange(ref solicitadaEm, Stopwatch.GetTimestamp()); }

    private static UdpClient AbrirPorta()
    {
        var udp = new UdpClient(AddressFamily.InterNetwork);
        try
        {
            udp.ExclusiveAddressUse = true;
            try { udp.Client.Bind(new IPEndPoint(IPAddress.Loopback, Convite.Porta)); }
            catch (SocketException e)
            {
                if (e.SocketErrorCode != SocketError.AddressAlreadyInUse && e.SocketErrorCode != SocketError.AccessDenied) throw;
                udp.Client.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            }
            return udp;
        }
        catch { udp.Close(); throw; }
    }

    private async Task<byte[]> RenovarInfoAsync()
    {
        try
        {
            using (var canal = await CanalJogo.AbrirValidadoAsync(idServidor, UsaUdp, UsoEntrada.Medir, CancellationToken.None).ConfigureAwait(false))
                return canal.InfoLocal(Endereco.Port);
        }
        catch (Exception erro) { if (!CanalWebSocket.ErroDeRede(erro)) throw; return null; }
    }

    private static int TipoConsulta(byte[] dados)
    {
        if (dados.Length < 5 || dados[0] != 255 || dados[1] != 255 || dados[2] != 255 || dados[3] != 255) return 0;
        if (dados[4] == 0x54 && (dados.Length == 25 || dados.Length == 29)) return 0x54;
        return dados[4] == 0x55 || dados[4] == 0x56 ? dados[4] : 0;
    }

    private async Task AtenderAsync(CanalJogo inicial)
    {
        var sessoes = new Dictionary<IPEndPoint, Sessao>();
        var inativo = Stopwatch.StartNew();
        byte[] info = inicial.InfoLocal(Endereco.Port);
        var idadeInfo = Stopwatch.StartNew();
        var ultimaConsulta = Stopwatch.StartNew();
        Task<byte[]> renovacao = null;
        Task<UdpReceiveResult> leitura = null;
        try
        {
            leitura = local.ReceiveAsync();
            var limpeza = Task.Delay(5000);
            while (true)
            {
                await Task.WhenAny(leitura, limpeza).ConfigureAwait(false);
                if (limpeza.IsCompleted)
                {
                    // Dá tempo para a Steam abrir o jogo. Depois da desconexão,
                    // dois minutos sem UDP do cliente encerram a ponte licenciada.
                    long desdeClique = Stopwatch.GetTimestamp() - Interlocked.Read(ref solicitadaEm);
                    if (inativo.Elapsed.TotalSeconds >= 120 && desdeClique >= 300L * Stopwatch.Frequency) break;
                    // O backend pode fechar um canal ocioso antes de o CS2 abrir.
                    // Um jogo que demore a iniciar recebe um novo canal validado.
                    if (inicial != null && inativo.Elapsed.TotalSeconds >= 20)
                    {
                        inicial.Dispose();
                        inicial = null;
                    }
                    foreach (var item in sessoes.Where(s => s.Value.Tempo.Elapsed.TotalSeconds >=
                        (s.Value.ApenasConsultas ? 8 : 120) || s.Value.Termino.IsCompleted).ToArray())
                    {
                        await item.Value.EncerrarAsync().ConfigureAwait(false);
                        sessoes.Remove(item.Key);
                    }
                    limpeza = Task.Delay(5000);
                }
                if (!leitura.IsCompleted) continue;
                UdpReceiveResult pacote;
                try { pacote = await leitura.ConfigureAwait(false); }
                catch (SocketException e)
                {
                    if (e.SocketErrorCode != SocketError.ConnectionReset) throw;
                    leitura = local.ReceiveAsync();
                    continue;
                }
                leitura = local.ReceiveAsync();
                if (!IPAddress.IsLoopback(pacote.RemoteEndPoint.Address) || pacote.Buffer.Length == 0 ||
                    pacote.Buffer.Length > CanalWebSocket.TamanhoMaximo) continue;
                int tipo = TipoConsulta(pacote.Buffer);
                if (tipo == 0x54)
                {
                    // A Steam consulta de várias portas descartáveis antes de abrir
                    // o CS2. Responda com o INFO real recente sem criar uma sessão de jogo.
                    if (renovacao != null && renovacao.IsCompleted)
                    {
                        byte[] nova = await renovacao.ConfigureAwait(false);
                        if (nova != null) { info = nova; idadeInfo.Restart(); }
                        renovacao = null;
                    }
                    if (renovacao == null && ultimaConsulta.Elapsed.TotalSeconds >= 15)
                    {
                        renovacao = RenovarInfoAsync();
                        ultimaConsulta.Restart();
                    }
                    if (idadeInfo.Elapsed.TotalSeconds <= 45)
                    {
                        lock (local) local.Send(info, info.Length, pacote.RemoteEndPoint);
                    }
                    continue;
                }
                Sessao sessao;
                if (!sessoes.TryGetValue(pacote.RemoteEndPoint, out sessao) || sessao.Termino.IsCompleted)
                {
                    if (sessao == null && sessoes.Count >= 8)
                    {
                        var antiga = sessoes.Where(s => s.Value.ApenasConsultas).OrderByDescending(s => s.Value.Tempo.Elapsed).FirstOrDefault();
                        if (antiga.Value == null) continue;
                        await antiga.Value.EncerrarAsync().ConfigureAwait(false);
                        sessoes.Remove(antiga.Key);
                    }
                    if (sessao != null) await sessao.EncerrarAsync().ConfigureAwait(false);
                    sessao = new Sessao(local, pacote.RemoteEndPoint, idServidor, UsaUdp, inicial);
                    inicial = null;
                    sessoes[pacote.RemoteEndPoint] = sessao;
                }
                sessao.Enfileirar(pacote.Buffer, tipo != 0);
                if (tipo == 0) inativo.Restart();
            }
        }
        catch (Exception erro) { if (!CanalWebSocket.ErroDeRede(erro)) throw; }
        finally
        {
            // Também encerra uma leitura UDP pendente enquanto esperava o jogo.
            local.Close();
            if (inicial != null) inicial.Dispose();
        }
        await Task.WhenAll(sessoes.Values.Select(s => s.EncerrarAsync())).ConfigureAwait(false);
        try { if (leitura != null) await leitura.ConfigureAwait(false); }
        catch (Exception erro) { if (!CanalWebSocket.ErroDeRede(erro)) throw; }
    }

    private sealed class Sessao
    {
        private readonly Queue<byte[]> fila = new Queue<byte[]>();
        private readonly SemaphoreSlim disponivel = new SemaphoreSlim(0);
        private readonly CancellationTokenSource parar = new CancellationTokenSource();
        internal readonly Stopwatch Tempo = Stopwatch.StartNew();
        internal readonly Task Termino;
        internal bool ApenasConsultas { get; private set; }

        internal Sessao(UdpClient local, IPEndPoint cliente, ulong id, bool udp, CanalJogo canal)
        {
            ApenasConsultas = true;
            Termino = TransmitirAsync(local, cliente, id, udp, canal);
        }

        internal async Task EncerrarAsync()
        {
            parar.Cancel();
            try { await Termino.ConfigureAwait(false); }
            finally { parar.Dispose(); disponivel.Dispose(); }
        }

        internal void Enfileirar(byte[] dados, bool consulta)
        {
            Tempo.Restart();
            if (!consulta) ApenasConsultas = false;
            lock (fila)
            {
                if (fila.Count >= 64) return;
                fila.Enqueue(dados);
            }
            disponivel.Release();
        }

        private async Task TransmitirAsync(UdpClient local, IPEndPoint cliente, ulong id, bool udp, CanalJogo canal)
        {
            try
            {
                if (canal == null) canal = await CanalJogo.AbrirValidadoAsync(id, udp, UsoEntrada.Jogar, parar.Token).ConfigureAwait(false);
                var ida = EnviarAsync(canal);
                var volta = canal.LerAsync(delegate(byte[] dados, int tamanho)
                {
                    lock (local) local.Send(dados, tamanho, cliente);
                }, parar.Token);
                await Task.WhenAny(ida, volta).ConfigureAwait(false);
                parar.Cancel();
                await Task.WhenAll(ida, volta).ConfigureAwait(false);
            }
            catch (Exception erro) { if (!CanalWebSocket.ErroDeRede(erro)) throw; }
            finally
            {
                if (canal != null) canal.Dispose();
            }
        }

        private async Task EnviarAsync(CanalJogo canal)
        {
            while (true)
            {
                await disponivel.WaitAsync(parar.Token).ConfigureAwait(false);
                byte[] dados;
                lock (fila) dados = fila.Dequeue();
                await canal.EnviarAsync(dados, dados.Length, parar.Token).ConfigureAwait(false);
            }
        }

    }
}
