using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

internal sealed class EstadoServidor
{
    internal string Mapa { get; private set; }
    internal int Jogadores { get; private set; }
    internal int Bots { get; private set; }
    internal int Capacidade { get; private set; }
    internal int PingMs { get; private set; }
    internal ulong SteamId { get; private set; }
    internal string Modo { get; private set; }
    internal EstatisticasMix Estatisticas { get; private set; }

    internal EstadoServidor(string mapa, int jogadores, int bots, int capacidade, int pingMs, string modo = null, EstatisticasMix estatisticas = null, ulong steamId = 0)
    {
        Mapa = mapa;
        Jogadores = jogadores;
        Bots = bots;
        Capacidade = capacidade;
        PingMs = pingMs;
        Modo = modo;
        Estatisticas = estatisticas;
        SteamId = steamId;
    }
}

internal sealed class EstatisticasMix
{
    internal long Partidas { get; private set; }
    internal long Jogadores { get; private set; }
    internal long Rodadas { get; private set; }
    internal long Empates { get; private set; }
    internal string UltimoMapa { get; private set; }
    internal int PlacarA { get; private set; }
    internal int PlacarB { get; private set; }

    internal EstatisticasMix(long partidas, long jogadores, long rodadas, long empates, string ultimoMapa = null, int placarA = 0, int placarB = 0)
    {
        Partidas = partidas;
        Jogadores = jogadores;
        Rodadas = rodadas;
        Empates = empates;
        UltimoMapa = ultimoMapa;
        PlacarA = placarA;
        PlacarB = placarB;
    }

    internal static EstatisticasMix Ler(string palavras)
    {
        string[] tags = palavras.Split(',');
        string[] totais = Campos(tags, "s100v1-", 4);
        int partidas, jogadores, empates;
        long rodadas;
        if (totais == null || !Numero(totais[0], out partidas) || !Numero(totais[1], out jogadores) ||
            !long.TryParse(totais[2], NumberStyles.None, CultureInfo.InvariantCulture, out rodadas) ||
            !Numero(totais[3], out empates) || empates > partidas || rodadas < partidas ||
            (partidas == 0 ? rodadas != 0 || jogadores != 0 : jogadores < 2 || jogadores > 10L * partidas))
            return null;

        string[] ultima = Campos(tags, "u100v1-", 3);
        int a, b;
        if (partidas > 0 && ultima != null && MapaValido(ultima[0]) &&
            Numero(ultima[1], out a) && Numero(ultima[2], out b) && (a > 0 || b > 0) &&
            (long)a + b <= rodadas && (a == b ? empates > 0 : empates < partidas))
            return new EstatisticasMix(partidas, jogadores, rodadas, empates, ultima[0], a, b);
        return new EstatisticasMix(partidas, jogadores, rodadas, empates);
    }

    private static string[] Campos(string[] tags, string prefixo, int quantidade)
    {
        string[] resultado = null;
        foreach (string item in tags)
        {
            string tag = item.Trim();
            if (!tag.StartsWith(prefixo, StringComparison.Ordinal)) continue;
            if (resultado != null || !tag.EndsWith("z", StringComparison.Ordinal)) return null;
            resultado = tag.Substring(prefixo.Length, tag.Length - prefixo.Length - 1).Split('-');
            if (resultado.Length != quantidade) return null;
        }
        return resultado;
    }

    private static bool Numero(string texto, out int valor)
    {
        return int.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out valor);
    }

    private static bool MapaValido(string mapa)
    {
        // Nomes simples dos mapas competitivos; nenhum texto arbitrário da rede vai para a janela.
        if (!mapa.StartsWith("de_", StringComparison.Ordinal) || mapa.Length < 4 || mapa.Length > 32) return false;
        for (int n = 3; n < mapa.Length; n++)
        {
            char c = mapa[n];
            if ((c < 'a' || c > 'z') && (c < '0' || c > '9') && c != '_') return false;
        }
        return true;
    }
}

internal static class ConsultaServidor
{
    private const int AppIdCs2 = 730;

    internal static EstadoServidor Consultar(IPAddress ip, int porta, int esperaMs = 1500)
    {
        return Consultar(ip, porta, esperaMs, CancellationToken.None);
    }

    internal static EstadoServidor Consultar(IPAddress ip, int porta, int esperaMs, CancellationToken token)
    {
        if (ip == null)
            throw new ArgumentNullException("ip");

        using (var socket = new Socket(ip.AddressFamily, SocketType.Dgram, ProtocolType.Udp))
        using (token.Register(socket.Dispose))
        {
            token.ThrowIfCancellationRequested();
            socket.SendTimeout = esperaMs;
            socket.ReceiveTimeout = esperaMs;
            socket.Connect(new IPEndPoint(ip, porta));

            return Consultar(delegate(byte[] pedido, int tamanho, byte[] resposta)
            {
                socket.Send(pedido, tamanho, SocketFlags.None);
                return socket.Receive(resposta);
            });
        }
    }

    internal static EstadoServidor Consultar(Func<byte[], int, byte[], int> trocar)
    {
        byte[] mensagem = Encoding.ASCII.GetBytes("TSource Engine Query\0");
        byte[] pedido = new byte[4 + mensagem.Length + 4];
        for (int i = 0; i < 4; i++) pedido[i] = 0xff;
        Buffer.BlockCopy(mensagem, 0, pedido, 4, mensagem.Length);
        int tamanhoPedido = 4 + mensagem.Length;

        // Cronometra uma viagem de ida e volta ao CS2.
        // O desafio inicial, quando exigido, não duplica a latência exibida.
        byte[] resposta = new byte[4096];
        while (true)
        {
            var tempo = Stopwatch.StartNew();
            int tamanho = trocar(pedido, tamanhoPedido, resposta);
            tempo.Stop();
            if (tamanho < 5 || resposta[0] != 0xff || resposta[1] != 0xff ||
                resposta[2] != 0xff || resposta[3] != 0xff)
                throw new InvalidDataException("Cabeçalho A2S_INFO inválido.");
            if (resposta[4] == 0x49)
                return LerEstado(resposta, tamanho, (int)Math.Ceiling(tempo.Elapsed.TotalMilliseconds));
            if (resposta[4] != 0x41 || tamanho != 9 || tamanhoPedido == pedido.Length)
                throw new InvalidDataException("Resposta inesperada à consulta do CS2.");
            Buffer.BlockCopy(resposta, 5, pedido, tamanhoPedido, 4);
            tamanhoPedido += 4;
        }
    }

    private static EstadoServidor LerEstado(byte[] dados, int tamanho, int pingMs)
    {
        int posicao = 6; // Cabeçalho, tipo e versão do protocolo.
        LerTexto(dados, tamanho, ref posicao); // Nome do servidor.
        string mapa = LerTexto(dados, tamanho, ref posicao);
        LerTexto(dados, tamanho, ref posicao); // Diretório do jogo.
        LerTexto(dados, tamanho, ref posicao); // Descrição do jogo.

        // AppID (little endian), jogadores, capacidade, bots, tipo, SO, senha e VAC.
        if (tamanho - posicao < 9)
            throw new InvalidDataException("Resposta A2S_INFO incompleta.");
        if ((dados[posicao] | (dados[posicao + 1] << 8)) != AppIdCs2)
            throw new InvalidDataException("O servidor respondeu por outro jogo.");
        int jogadores = dados[posicao + 2];
        int capacidade = dados[posicao + 3];
        int bots = dados[posicao + 4];
        posicao += 9;
        LerTexto(dados, tamanho, ref posicao); // A versão encerra a parte obrigatória.

        if (mapa.Length == 0 || mapa.Length > 260 || Array.Exists(mapa.ToCharArray(), char.IsControl))
            throw new InvalidDataException("Nome de mapa inválido na resposta do servidor.");

        string modo = null;
        ulong steamId = 0;
        EstatisticasMix estatisticas = null;
        if (posicao < tamanho)
        {
            int campos = dados[posicao++];
            if ((campos & 0x80) != 0) Avancar(2, tamanho, ref posicao); // Porta do jogo.
            if ((campos & 0x10) != 0)
            {
                Avancar(8, tamanho, ref posicao);
                steamId = BitConverter.ToUInt64(dados, posicao - 8);
            }
            if ((campos & 0x40) != 0)
            {
                Avancar(2, tamanho, ref posicao); // Porta e nome do SourceTV.
                LerTexto(dados, tamanho, ref posicao);
            }
            if ((campos & 0x20) != 0)
            {
                string palavras = LerTexto(dados, tamanho, ref posicao);
                modo = NomeDoModo(palavras, mapa);
                estatisticas = EstatisticasMix.Ler(palavras);
            }
            if ((campos & 0x01) != 0) Avancar(8, tamanho, ref posicao); // GameID.
        }
        return new EstadoServidor(mapa, jogadores, bots, capacidade, pingMs, modo, estatisticas, steamId);
    }

    private static void Avancar(int bytes, int tamanho, ref int posicao)
    {
        if (bytes > tamanho - posicao) throw new InvalidDataException("Campo A2S_INFO incompleto.");
        posicao += bytes;
    }

    internal static string NomeDoModo(string palavras, string mapa)
    {
        string id = null;
        foreach (string item in palavras.Split(','))
        {
            string tag = item.Trim();
            if (!tag.StartsWith("m100v1-", StringComparison.Ordinal)) continue;
            if (id != null) return null;
            id = tag.Substring(7);
        }
        if (id == null) return null;
        switch (id)
        {
            case "competitive": return "Competitivo";
            case "mix1": return ArenaDoMix.Reconhecer(mapa) == null ? "Mix 1x1" : "Duelo 1x1";
            case "casual": return "Casual";
            case "dm": return "Mata-mata";
            case "dm-pistol": return "DM · Pistolas";
            case "dm-awp": return "DM · AWP";
            case "dm-rifle": return "DM · Rifles";
            case "gungame": return "GunGame";
            case "wingman": return "Wingman";
            case "retakes": return "Retakes";
        }
        for (int n = 1; n <= 5; n++)
        {
            if (id == "mix" + n) return "Mix " + n + "x" + n;
            if (id == "short" + n) return "Curto " + n + "x" + n;
        }
        return null;
    }

    private static string LerTexto(byte[] dados, int tamanho, ref int posicao)
    {
        if (posicao >= tamanho)
            throw new InvalidDataException("Resposta A2S_INFO incompleta.");
        int fim = Array.IndexOf(dados, (byte)0, posicao, tamanho - posicao);
        if (fim < 0)
            throw new InvalidDataException("Texto incompleto na resposta A2S_INFO.");
        string valor = Encoding.UTF8.GetString(dados, posicao, fim - posicao);
        posicao = fim + 1;
        return valor;
    }
}
