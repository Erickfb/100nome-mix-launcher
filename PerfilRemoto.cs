#if PERFIL_REMOTO || PERFIL_TRANSICAO
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;

#if PERFIL_REMOTO
internal static partial class PerfilInstancia
{
    internal const string EnderecoLocalCadastro = "";
    internal static string PastaDados { get { return PerfilRemoto.PrepararInicio().Pasta; } }
    internal static string ChaveLocalPC { get { return PerfilRemoto.PrepararInicio().ChavePC; } }
    internal static string ModuloEmissor { get { return PerfilRemoto.PrepararInicio().Emissor; } }
    internal static string DominioPrincipal { get { return PerfilRemoto.Perfil.DominioPrincipal; } }
    internal static string DominioTerceiro { get { return PerfilRemoto.Perfil.DominioTerceiro; } }
    internal static ulong SteamIdServidor { get { return PerfilRemoto.Perfil.SteamIdServidor; } }
    internal static string CanalDiscord { get { return PerfilRemoto.Perfil.CanalDiscord; } }
}
#endif

internal static class PerfilRemoto
{
    private static readonly object GuardaInicio = new object();
    private static Inicio inicio;
    internal static DadosPerfil Perfil { get; private set; }
    private static Dictionary<string, byte[]> artes;

    [DataContract]
    internal sealed class Inicio
    {
        [DataMember(Name="pastaDados", IsRequired=true)] internal string Pasta { get; set; }
        [DataMember(Name="chaveLocalPC", IsRequired=true)] internal string ChavePC { get; set; }
        [DataMember(Name="moduloEmissor", IsRequired=true)] internal string Emissor { get; set; }
        [DataMember(Name="moduloAtualizador", IsRequired=true)] internal string Atualizador { get; set; }
    }

    [DataContract]
    internal sealed class Noticia
    {
        [DataMember(Name="titulo", IsRequired=true)] public string Titulo { get; private set; }
        [DataMember(Name="texto", IsRequired=true)] public string Texto { get; private set; }
    }

    [DataContract]
    private sealed class Comando
    {
        [DataMember(Name="grupo", IsRequired=true)] internal string Grupo { get; set; }
        [DataMember(Name="comando", IsRequired=true)] internal string Nome { get; set; }
        [DataMember(Name="descricao", IsRequired=true)] internal string Descricao { get; set; }
        [DataMember(Name="exemplo")] internal string Exemplo { get; set; }
    }

    [DataContract]
    internal sealed class DadosPerfil
    {
        [DataMember(Name="versao", IsRequired=true)] private int versao { get; set; }
        [DataMember(Name="dominioPrincipal", IsRequired=true)] internal string DominioPrincipal { get; set; }
        [DataMember(Name="dominioTerceiro", IsRequired=true)] internal string DominioTerceiro { get; set; }
        [DataMember(Name="steamIdServidor", IsRequired=true)] private string steamId { get; set; }
        [DataMember(Name="canalDiscord", IsRequired=true)] internal string CanalDiscord { get; set; }
        [DataMember(Name="nomeGM", IsRequired=true)] private string nomeGM { get; set; }
        [DataMember(Name="comandos", IsRequired=true)] private Comando[] comandos { get; set; }
        [DataMember(Name="noticias", IsRequired=true)] internal Noticia[] Noticias { get; set; }
        internal ulong SteamIdServidor;
        internal ComandoDoMix[] Comandos;

        internal void Validar()
        {
            Uri discord;
            if (versao != 1 || !DominioValido(DominioPrincipal) || !DominioValido(DominioTerceiro) ||
                !ulong.TryParse(steamId, NumberStyles.None, CultureInfo.InvariantCulture, out SteamIdServidor) ||
                SteamIdServidor == 0 || !TextoValido(nomeGM, 100) || !TextoValido(CanalDiscord, 512) ||
                !Uri.TryCreate(CanalDiscord, UriKind.Absolute, out discord) || discord.Scheme != Uri.UriSchemeHttps ||
                !string.IsNullOrEmpty(discord.UserInfo) || !discord.IsDefaultPort ||
                !string.IsNullOrEmpty(discord.Query) || !string.IsNullOrEmpty(discord.Fragment))
                throw new InvalidDataException("Entradas ou configuração da comunidade inválidas.");
            if (Noticias == null || Noticias.Length > 128 || Noticias.Any(n => n == null ||
                !TextoValido(n.Titulo, 180) || !TextoValido(n.Texto, 3000)) ||
                comandos == null || comandos.Length < 1 || comandos.Length > 512 ||
                comandos.Any(c => c == null || !TextoValido(c.Grupo, 80) || !TextoValido(c.Nome, 256) ||
                    !TextoValido(c.Descricao, 3000) || (c.Exemplo != null && !TextoValido(c.Exemplo, 384))))
                throw new InvalidDataException("Guia ou notícias da comunidade inválidos.");
            Comandos = comandos.Select(c => new ComandoDoMix(c.Grupo, c.Nome, c.Descricao, c.Exemplo)).ToArray();
        }
    }

    internal static Inicio PrepararInicio()
    {
        lock (GuardaInicio)
        {
            if (inicio != null) return inicio;
#if PERFIL_REMOTO
            var host = PerfilInstancia.HostCadastro;
#else
            var host = PerfilInstancia.DominioPrincipal;
#endif
            var q = (HttpWebRequest)WebRequest.Create(CadastroAutomatico.Endereco(host, "/v4/inicio"));
            q.Proxy = null;
            q.AllowAutoRedirect = false;
            q.Timeout = q.ReadWriteTimeout = 5000;
            q.ServerCertificateValidationCallback = CadastroAutomatico.Certificado;
            using (var r = (HttpWebResponse)q.GetResponse())
            using (var leitor = new BinaryReader(r.GetResponseStream()))
            {
                if (r.StatusCode != HttpStatusCode.OK || r.ContentLength > 4096) throw new InvalidDataException();
                var bytes = leitor.ReadBytes(4097);
                if (bytes.Length > 4096) throw new InvalidDataException();
                var novo = LerJson<Inicio>(bytes);
                if (!Regex.IsMatch(novo.Pasta ?? "", @"\A[A-Za-z0-9][A-Za-z0-9_-]{0,63}\z") ||
                    Regex.IsMatch(novo.Pasta, @"\A(CON|PRN|NUL|AUX|COM[0-9]|LPT[0-9])\z", RegexOptions.IgnoreCase) ||
                    !Regex.IsMatch(novo.ChavePC ?? "", @"\A[A-Za-z0-9][A-Za-z0-9_.-]{0,95}\z") ||
                    !ModuloValido(novo.Emissor) || !ModuloValido(novo.Atualizador))
                    throw new InvalidDataException("Identificação da central inválida.");
                inicio = novo;
                return inicio;
            }
        }
    }

    private static bool DominioValido(string s)
    {
        return !string.IsNullOrEmpty(s) && s.Length <= 253 &&
            Regex.IsMatch(s, @"\A[A-Za-z0-9.-]+\z") && Uri.CheckHostName(s) == UriHostNameType.Dns;
    }

    private static bool ModuloValido(string texto)
    {
        if (texto == null || texto.Length != 512) return false;
        var b = Convert.FromBase64String(texto);
        return b.Length == 384 && (b[0] & 128) != 0 && (b[b.Length-1] & 1) == 1;
    }

    internal static string ModuloAtualizador() { return PrepararInicio().Atualizador; }

    private static bool TextoValido(string texto, int maximo)
    {
        return !string.IsNullOrWhiteSpace(texto) && texto.Length <= maximo && !texto.Any(char.IsControl);
    }

    internal static bool PedirConsentimento(Window dono, bool novo)
    {
        string aviso = novo
            ? "O launcher vai calcular o código deste PC e enviá-lo à central do mix, junto com o apelido da Steam quando disponível, para consultar ou solicitar seu cadastro. "
            : "A central já reconhece o cadastro deste PC. ";
        aviso += "Para abrir o perfil do grupo pela primeira vez, o launcher envia por HTTPS a chave pública do PC e o identificador da instalação do Windows (MachineGuid). A central confere o cadastro, descarta esse identificador e guarda a impressão da chave. Nas próximas aberturas, o PC confirma sua identidade com uma assinatura. Deseja continuar?";
        const string titulo = "Autorizar este PC no 100Nome Mix";
        return (dono == null
            ? MessageBox.Show(aviso, titulo, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
            : MessageBox.Show(dono, aviso, titulo, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)) == MessageBoxResult.Yes;
    }

    internal static bool Carregar(bool consentiu, CancellationToken token)
    {
        byte[] dados = AutorizacaoRota.ObterPerfil(consentiu, token);
        if (dados == null) return false;
        Abrir(dados, token);
        return true;
    }

    private static T LerJson<T>(byte[] bytes) where T : class
    {
        using (var stream = new MemoryStream(bytes, false))
        {
            var resultado = (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
            if (resultado == null) throw new InvalidDataException("Configuração vazia.");
            return resultado;
        }
    }

    internal static void Abrir(byte[] zip, CancellationToken token)
    {
        const int limitePacote = 16 * 1024 * 1024;
        if (zip == null || zip.Length == 0 || zip.Length > limitePacote) throw new InvalidDataException("Tamanho de perfil inválido.");
        var imagens = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var nomes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        byte[] cadastro = null, canal = null;
        long total = 0;
        using (var stream = new MemoryStream(zip, false))
        using (var pacote = new ZipArchive(stream, ZipArchiveMode.Read))
        {
            if (pacote.Entries.Count > 64) throw new InvalidDataException("Perfil com arquivos demais.");
            foreach (var item in pacote.Entries)
            {
                token.ThrowIfCancellationRequested();
                string nome = item.FullName;
                bool perfil = nome == "perfil.json", atualizacao = nome == "canal.json";
                string recurso = NomeDaArte(nome);
                long limite = perfil ? 256 * 1024 : atualizacao ? 4096 : 4 * 1024 * 1024;
                if (!nomes.Add(nome) || (!perfil && !atualizacao && recurso == null) ||
                    item.Length <= 0 || item.Length > limite || (total += item.Length) > 32 * 1024 * 1024)
                    throw new InvalidDataException("Arquivo inesperado no perfil.");
                using (var leitor = new BinaryReader(item.Open()))
                {
                    var bytes = leitor.ReadBytes((int)item.Length + 1);
                    if (bytes.Length != item.Length) throw new InvalidDataException("Arquivo incompleto no perfil.");
                    if (perfil) cadastro = bytes;
                    else if (atualizacao) canal = bytes;
                    else { ValidarImagem(nome, bytes); imagens.Add(recurso, bytes); }
                }
            }
        }
        if (cadastro == null || canal == null || !imagens.ContainsKey("marca") || !imagens.ContainsKey("mapa"))
            throw new InvalidDataException("Perfil incompleto.");
        var pronto = LerJson<DadosPerfil>(cadastro);
        pronto.Validar();
        token.ThrowIfCancellationRequested();
        Atualizacao.ConfigurarCanal(canal);
        Perfil = pronto;
        artes = imagens;
    }

    private static string NomeDaArte(string nome)
    {
        if (nome == "marca.png") return "marca";
        if (nome == "marca.ico") return "icone";
        if (nome == "mapa.jpg") return "mapa";
        var m = Regex.Match(nome, @"\Amapas/([a-z0-9_]{1,64})\.jpg\z");
        if (m.Success) return "mapa." + m.Groups[1].Value;
        m = Regex.Match(nome, @"\Apatentes/(skillgroup(?:[1-9]|1[0-8]))\.png\z");
        return m.Success ? "patente." + m.Groups[1].Value : null;
    }

    private static void ValidarImagem(string nome, byte[] bytes)
    {
        using (var s = new MemoryStream(bytes, false))
        {
            var d = BitmapDecoder.Create(s, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            if ((nome.EndsWith(".png", StringComparison.Ordinal) && !(d is PngBitmapDecoder)) ||
                (nome.EndsWith(".jpg", StringComparison.Ordinal) && !(d is JpegBitmapDecoder)) ||
                (nome.EndsWith(".ico", StringComparison.Ordinal) && !(d is IconBitmapDecoder)) ||
                d.Frames.Count < 1 || d.Frames.Count > 20 ||
                d.Frames.Any(f => f.PixelWidth < 1 || f.PixelHeight < 1 || f.PixelWidth > 4096 || f.PixelHeight > 4096 ||
                    (long)f.PixelWidth * f.PixelHeight > 12 * 1024 * 1024))
                throw new InvalidDataException("Formato ou dimensões da imagem inválidos.");
        }
    }

    internal static string[] NomesDasArtes() { return artes.Keys.ToArray(); }
    internal static Stream AbrirArte(string nome)
    {
        byte[] bytes;
        return nome != null && artes.TryGetValue(nome, out bytes) ? new MemoryStream(bytes, false) : null;
    }
}
#endif
