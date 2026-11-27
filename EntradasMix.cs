using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

internal sealed class EntradaMix
{
    internal static readonly EntradaMix Direta = new EntradaMix(1, Convite.Dominio, Convite.Porta);
    internal static readonly EntradaMix Terceira = new EntradaMix(3, PerfilInstancia.DominioTerceiro, Convite.Porta);
    internal static readonly EntradaMix[] Todas = { Direta, Terceira };

    internal readonly int Numero;
    internal string Nome { get { return "Entrada " + Numero; } }
    internal readonly string Dominio;
    internal readonly int Porta;
    internal string Endereco { get { return Dominio + ":" + Porta; } }

    private EntradaMix(int numero, string dominio, int porta)
    {
        Numero = numero;
        Dominio = dominio;
        Porta = porta;
    }

    internal static bool ErroDeRede(Exception erro)
    {
        return erro is IOException || erro is InvalidDataException || erro is SocketException ||
            erro is WebException || erro is OperationCanceledException || erro is ObjectDisposedException ||
            erro is System.Security.Cryptography.CryptographicException || erro is PlatformNotSupportedException ||
            erro is System.Text.DecoderFallbackException;
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
                if (!ErroDeRede(erro)) throw;
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
    internal bool PodeConectar { get { return Destino != null; } }

    internal MedicaoEntrada(EntradaMix entrada) : this(entrada, null) { Medindo = true; }

    internal MedicaoEntrada(EntradaMix entrada, IPEndPoint destino, EstadoServidor estado = null, int amostras = 0, int pingMs = 0)
    {
        Entrada = entrada;
        Destino = destino;
        Estado = estado;
        Amostras = amostras;
        PingMs = pingMs;
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
    internal readonly MedicaoEntrada Terceira;
    internal MedicaoEntrada MelhorConfirmada(EntradaMix preferida)
    {
        var validas = new[] { Direta, Terceira }.Where(m => m.Estado != null && m.PodeConectar);
        return validas.OrderBy(m => m.PingMs)
                      .ThenByDescending(m => m.Entrada == preferida).FirstOrDefault();
    }

    internal MedicaoEntrada MenorPing
    {
        get
        {
            var validas = new[] { Direta, Terceira }.Where(m => m.Estado != null && m.PodeConectar).ToArray();
            if (validas.Length < 2 || validas.Any(m => m.Amostras < 2)) return null;
            var melhores = validas.OrderBy(m => m.PingMs).ToArray();
            return melhores[0].PingMs < melhores[1].PingMs ? melhores[0] : null;
        }
    }

    internal ComparacaoEntradas(MedicaoEntrada[] direta, MedicaoEntrada[] terceira)
    {
        Direta = MedicaoEntrada.Melhor(direta);
        ulong id = Direta.Estado != null && Direta.Estado.SteamId != 0 ? Direta.Estado.SteamId : SteamIdDoMix;
        var caminhos = terceira.Where(m => m.Destino != null && m.Estado != null && m.Estado.SteamId == id).ToArray();
        Terceira = caminhos.Length > 0 ? MedicaoEntrada.Melhor(caminhos) :
            terceira.FirstOrDefault(m => m.Medindo) ?? new MedicaoEntrada(EntradaMix.Terceira, null);
    }

    internal MedicaoEntrada MedicaoDe(EntradaMix entrada)
    {
        return entrada == EntradaMix.Terceira ? Terceira : Direta;
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
            return new MedicaoEntrada(atual.Entrada, atual.Destino, atual.Estado, tempos.Length, mediana);
        }).ToArray();
    }
}

// Cada entrada tem no máximo uma leitura em curso enquanto a janela tem foco.
internal sealed class SondagemEntradas : IDisposable
{
    private readonly HistoricoPing direta = new HistoricoPing(EntradaMix.Direta);
    private readonly HistoricoPing terceira = new HistoricoPing(EntradaMix.Terceira);
    private readonly CancellationTokenSource parar = new CancellationTokenSource();
    private readonly Action<ComparacaoEntradas> mostrar;
    private Task leituraDireta;
    private Task leituraTerceira;
    internal ComparacaoEntradas Atual { get { return new ComparacaoEntradas(direta.Ultimas, terceira.Ultimas); } }

    internal SondagemEntradas(Action<ComparacaoEntradas> mostrar)
    {
        this.mostrar = mostrar;
    }

    internal void Medir()
    {
        if (leituraDireta == null || leituraDireta.IsCompleted)
            leituraDireta = RegistrarAsync(direta, EntradaMix.Direta.MedirDiretaAsync(parar.Token));
        if (leituraTerceira == null || leituraTerceira.IsCompleted)
            leituraTerceira = RegistrarAsync(terceira, EntradaMix.Terceira.MedirDiretaAsync(parar.Token));
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

    internal async Task<ComparacaoEntradas> AguardarAsync()
    {
        await Task.WhenAll(leituraDireta, leituraTerceira);
        return Atual;
    }

    internal async Task<ComparacaoEntradas> AguardarEscolhaAsync(MedicaoEntrada escolha)
    {
        var tarefa = escolha.Entrada == EntradaMix.Terceira ? leituraTerceira : leituraDireta;
        if (tarefa != null) await tarefa;
        return Atual;
    }

    public void Dispose()
    {
        parar.Cancel();
        parar.Dispose();
    }
}
