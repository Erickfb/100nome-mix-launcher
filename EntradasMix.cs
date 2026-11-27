using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

internal sealed class EntradaMix
{
    internal static readonly EntradaMix Direta = new EntradaMix(1, Convite.Dominio, Convite.Porta);
    internal static readonly EntradaMix Ponte = new EntradaMix(2, PerfilInstancia.DominioPonte, 443, true);
    internal static readonly EntradaMix Terceira = new EntradaMix(3, PerfilInstancia.DominioTerceiro, Convite.Porta);
    internal static readonly EntradaMix[] Todas = { Direta, Ponte, Terceira };

    internal readonly int Numero;
    internal string Nome { get { return "Entrada " + Numero; } }
    internal readonly string Dominio;
    internal readonly int Porta;
    internal readonly bool UsaPonte;
    internal string Endereco { get { return UsaPonte ? Dominio : Dominio + ":" + Porta; } }

    private EntradaMix(int numero, string dominio, int porta, bool usaPonte = false)
    {
        Numero = numero;
        Dominio = dominio;
        Porta = porta;
        UsaPonte = usaPonte;
    }

    internal async Task<MedicaoEntrada[]> MedirDiretaAsync(CancellationToken token)
    {
        var dns = ResolverAsync();
        if (await Task.WhenAny(dns, Task.Delay(2000, token)).ConfigureAwait(false) != dns)
            return new[] { new MedicaoEntrada(this, null) };
        var ips = await dns.ConfigureAwait(false);
        if (ips.Length == 0) return new[] { new MedicaoEntrada(this, null) };

        var tarefas = ips.Select(ip => Task.Run(() =>
        {
            var destino = new IPEndPoint(ip, Porta);
            try
            {
                var estado = ConsultaServidor.Consultar(ip, Porta, 900, token);
                return new MedicaoEntrada(this, destino, estado, 1, estado.PingMs);
            }
            catch (Exception erro)
            {
                if (!CanalWebSocket.ErroDeRede(erro)) throw;
                return new MedicaoEntrada(this, destino);
            }
        }, token));
        return await Task.WhenAll(tarefas).ConfigureAwait(false);
    }

    private async Task<IPAddress[]> ResolverAsync()
    {
        try
        {
            var ips = await Dns.GetHostAddressesAsync(Dominio).ConfigureAwait(false);
            return ips.Where(ip => ip.AddressFamily == AddressFamily.InterNetwork).Distinct().ToArray();
        }
        catch (SocketException) { return new IPAddress[0]; }
    }

}

internal sealed class MedicaoEntrada
{
    internal readonly EntradaMix Entrada;
    internal readonly IPEndPoint Destino;
    internal readonly EstadoServidor Estado;
    internal readonly int Amostras;
    internal readonly int PingMs;
    internal readonly bool Medindo;
    internal readonly bool UsaUdp;
    internal string Transporte { get { return UsaUdp ? "UDP direto" : "via ponte"; } }
    internal bool PodeConectar { get { return Entrada.UsaPonte ? Estado != null : Destino != null; } }

    internal MedicaoEntrada(EntradaMix entrada) : this(entrada, null) { Medindo = true; }

    internal MedicaoEntrada(EntradaMix entrada, IPEndPoint destino, EstadoServidor estado = null, int amostras = 0, int pingMs = 0)
        : this(entrada, destino, estado, amostras, pingMs, false) { }

    internal MedicaoEntrada(EntradaMix entrada, IPEndPoint destino, EstadoServidor estado, int amostras, int pingMs, bool udp)
    {
        Entrada = entrada;
        Destino = destino;
        Estado = estado;
        Amostras = amostras;
        PingMs = pingMs;
        UsaUdp = udp;
    }

    internal static MedicaoEntrada Melhor(params MedicaoEntrada[] medicoes)
    {
        // Prefira duas ou três respostas. Uma única resposta ainda serve quando
        // os outros caminhos falham. A ordem de entrada desempata a mesma mediana.
        return medicoes.OrderByDescending(m => Math.Min(2, m.Amostras))
                       .ThenBy(m => m.PingMs).First();
    }
}

internal sealed class ComparacaoEntradas
{
    // SteamID público anunciado pelo dedicado do mix na consulta direta A2S.
    // Se o token do servidor mudar, a rota direta informa a identidade atual.
    internal const ulong SteamIdDoMix = PerfilInstancia.SteamIdServidor;
    internal readonly MedicaoEntrada Direta;
    internal readonly MedicaoEntrada Ponte;
    internal readonly MedicaoEntrada Terceira;
    internal MedicaoEntrada MelhorConfirmada(EntradaMix preferida)
    {
        var validas = new[] { Direta, Ponte, Terceira }.Where(m => m.Estado != null && m.PodeConectar);
        return validas.OrderBy(m => m.PingMs)
                      .ThenByDescending(m => m.Entrada == preferida).FirstOrDefault();
    }

    internal MedicaoEntrada MenorPing
    {
        get
        {
            var validas = new[] { Direta, Ponte, Terceira }.Where(m => m.Estado != null && m.PodeConectar).ToArray();
            if (validas.Length < 2 || validas.Any(m => m.Amostras < 2)) return null;
            var melhores = validas.OrderBy(m => m.PingMs).ToArray();
            return melhores[0].PingMs < melhores[1].PingMs ? melhores[0] : null;
        }
    }

    internal ComparacaoEntradas(MedicaoEntrada[] direta, MedicaoEntrada[] ponte, MedicaoEntrada[] terceira)
    {
        Direta = MedicaoEntrada.Melhor(direta);
        ulong id = Direta.Estado != null && Direta.Estado.SteamId != 0 ? Direta.Estado.SteamId : SteamIdDoMix;
        var caminhos = terceira.Where(m => m.Destino != null && m.Estado != null && m.Estado.SteamId == id).ToArray();
        Terceira = caminhos.Length > 0 ? MedicaoEntrada.Melhor(caminhos) :
            terceira.FirstOrDefault(m => m.Medindo) ?? new MedicaoEntrada(EntradaMix.Terceira, null);
        // O provedor pode reatribuir um endereço de relay a outro servidor de CS2.
        var confirmadas = ponte.Where(m => m.Estado != null && m.Estado.SteamId == id).ToArray();
        var viaPonte = confirmadas.FirstOrDefault(m => !m.UsaUdp);
        var viaUdp = confirmadas.FirstOrDefault(m => m.UsaUdp);
        // Um caminho já confirmado continua disponível enquanto o outro negocia.
        // Com duas medianas, 8 ms de margem evitam trocar por pequenas oscilações.
        if (viaPonte == null) Ponte = viaUdp ?? ponte.FirstOrDefault(m => m.Medindo) ?? new MedicaoEntrada(ponte[0].Entrada, null);
        else if (viaUdp == null || viaUdp.Amostras < 2 ||
            (viaPonte.Amostras >= 2 && viaPonte.PingMs + 8 < viaUdp.PingMs)) Ponte = viaPonte;
        else Ponte = viaUdp;
    }

    internal MedicaoEntrada MedicaoDe(EntradaMix entrada)
    {
        if (entrada == EntradaMix.Terceira) return Terceira;
        return entrada.UsaPonte ? Ponte : Direta;
    }

    internal static async Task<ComparacaoEntradas> ConsultarAsync()
    {
        using (var sondagem = new SondagemEntradas(null))
        {
            ComparacaoEntradas resultado = null;
            for (int rodada = 0; rodada < 3; rodada++)
            {
                if (rodada > 0) await Task.Delay(120).ConfigureAwait(false);
                sondagem.Medir();
                resultado = await sondagem.AguardarAsync().ConfigureAwait(false);
            }
            return resultado;
        }
    }
}

// Guarda as três rodadas mais recentes de uma entrada. Se o DDNS mudar,
// o novo destino inicia o próprio histórico de latência.
internal sealed class HistoricoPing
{
    private readonly Queue<MedicaoEntrada[]> rodadas = new Queue<MedicaoEntrada[]>();
    internal MedicaoEntrada[] Ultimas { get; private set; }

    internal HistoricoPing(EntradaMix entrada)
    {
        Ultimas = new[] { new MedicaoEntrada(entrada) };
    }

    internal void Registrar(MedicaoEntrada[] atuais)
    {
        if (rodadas.Count == 3) rodadas.Dequeue();
        rodadas.Enqueue(atuais);
        Ultimas = atuais.Select(atual =>
        {
            if (atual.Estado == null) return atual;
            var tempos = rodadas.SelectMany(rodada => rodada).Where(m => Equals(m.Destino, atual.Destino) &&
                m.Estado != null && m.Estado.SteamId == atual.Estado.SteamId).Select(m => m.PingMs).OrderBy(ms => ms).ToArray();
            int mediana = (tempos[(tempos.Length - 1) / 2] + tempos[tempos.Length / 2] + 1) / 2;
            return new MedicaoEntrada(atual.Entrada, atual.Destino, atual.Estado, tempos.Length, mediana, atual.UsaUdp);
        }).ToArray();
    }
}

// Uma sondagem dura um período de foco. O canal de medição é separado da
// ponte usada pela partida e cada entrada pode ter apenas uma leitura em curso.
internal sealed class SondagemEntradas : IDisposable
{
    private readonly HistoricoPing direta = new HistoricoPing(EntradaMix.Direta);
    private readonly HistoricoPing ponte = new HistoricoPing(EntradaMix.Ponte);
    private readonly HistoricoPing udp = new HistoricoPing(EntradaMix.Ponte);
    private readonly HistoricoPing terceira = new HistoricoPing(EntradaMix.Terceira);
    private readonly CancellationTokenSource parar = new CancellationTokenSource();
    private readonly Action<ComparacaoEntradas> mostrar;
    private CanalJogo canal;
    private CanalJogo canalUdp;
    private static long retomarPonte;
    private static long retomarUdp;
    private Task leituraDireta;
    private Task leituraPonte;
    private Task leituraUdp;
    private Task leituraTerceira;
    internal ComparacaoEntradas Atual { get { return new ComparacaoEntradas(direta.Ultimas, ponte.Ultimas.Concat(udp.Ultimas).ToArray(), terceira.Ultimas); } }

    internal SondagemEntradas(Action<ComparacaoEntradas> mostrar)
    {
        this.mostrar = mostrar;
    }

    internal static void RetomarEntrada2()
    {
        Interlocked.Exchange(ref retomarPonte, 0);
        Interlocked.Exchange(ref retomarUdp, 0);
    }

    internal void Medir()
    {
        if (leituraDireta == null || leituraDireta.IsCompleted)
            leituraDireta = RegistrarAsync(direta, EntradaMix.Direta.MedirDiretaAsync(parar.Token));
        if (leituraTerceira == null || leituraTerceira.IsCompleted)
            leituraTerceira = RegistrarAsync(terceira, EntradaMix.Terceira.MedirDiretaAsync(parar.Token));
        MedirCanal(ref leituraPonte, ponte, false, Interlocked.Read(ref retomarPonte));
        MedirCanal(ref leituraUdp, udp, true, Interlocked.Read(ref retomarUdp));
    }

    private void MedirCanal(ref Task leitura, HistoricoPing historico, bool viaUdp, long retomar)
    {
        if (leitura != null && !leitura.IsCompleted) return;
        if (Stopwatch.GetTimestamp() >= retomar)
            leitura = RegistrarAsync(historico, MedirPonteAsync(viaUdp, parar.Token));
        else if (historico.Ultimas[0].Medindo)
            historico.Registrar(new[] { new MedicaoEntrada(EntradaMix.Ponte, null) });
    }

    private async Task RegistrarAsync(HistoricoPing historico, Task<MedicaoEntrada[]> leitura)
    {
        try
        {
            var atuais = await leitura;
            if (parar.IsCancellationRequested) return;
            historico.Registrar(atuais);
            if (mostrar != null) mostrar(Atual);
        }
        catch (OperationCanceledException) { if (!parar.IsCancellationRequested) throw; }
    }

    private async Task<MedicaoEntrada[]> MedirPonteAsync(bool viaUdp, CancellationToken token)
    {
        try
        {
            var canalAtual = viaUdp ? canalUdp : canal;
            if (canalAtual == null)
            {
                canalAtual = await CanalJogo.AbrirAsync(viaUdp, UsoEntrada.Medir, token);
                if (token.IsCancellationRequested) { canalAtual.Dispose(); token.ThrowIfCancellationRequested(); }
                if (viaUdp) canalUdp = canalAtual; else canal = canalAtual;
            }
            var estado = await Task.Run(() => canalAtual.Consultar(token), token);
            return new[] { new MedicaoEntrada(EntradaMix.Ponte, null, estado, 1, estado.PingMs, viaUdp) };
        }
        catch (Exception erro)
        {
            if (!CanalWebSocket.ErroDeRede(erro)) throw;
            if (!token.IsCancellationRequested)
            {
                if (viaUdp) Interlocked.Exchange(ref retomarUdp, Stopwatch.GetTimestamp() + 60L * Stopwatch.Frequency);
                else Interlocked.Exchange(ref retomarPonte, Stopwatch.GetTimestamp() + 15L * Stopwatch.Frequency);
            }
            FecharCanal(viaUdp);
            return new[] { new MedicaoEntrada(EntradaMix.Ponte, null) };
        }
    }

    internal async Task<ComparacaoEntradas> AguardarAsync()
    {
        await Task.WhenAll(leituraDireta, leituraPonte ?? Task.FromResult(0), leituraTerceira, leituraUdp ?? Task.FromResult(0));
        return Atual;
    }

    internal async Task<ComparacaoEntradas> AguardarEscolhaAsync(MedicaoEntrada escolha)
    {
        var tarefa = escolha.Entrada == EntradaMix.Terceira ? leituraTerceira :
            escolha.Entrada.UsaPonte ? (escolha.UsaUdp ? leituraUdp : leituraPonte) : leituraDireta;
        if (tarefa != null) await tarefa;
        return Atual;
    }

    private void FecharCanal(bool viaUdp)
    {
        var encerrar = viaUdp ? canalUdp : canal;
        if (viaUdp) canalUdp = null; else canal = null;
        if (encerrar != null) encerrar.Dispose();
    }

    public void Dispose()
    {
        parar.Cancel();
        FecharCanal(false);
        FecharCanal(true);
        parar.Dispose();
    }
}
