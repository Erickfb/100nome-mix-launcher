using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

[DataContract]
internal sealed class AtualizacaoConfig
{
    [DataMember(Name = "edicao")] internal long Edicao { get; set; }
    [DataMember(Name = "origem")] internal string Origem { get; set; }
    [DataMember(Name = "modulo")] internal string Modulo { get; set; }
    [DataMember(Name = "expoente")] internal string Expoente { get; set; }
    [DataMember(Name = "chave")] internal string Chave { get; set; }
    internal string Instancia { get; private set; }
    internal bool ExePublico { get { return Chave == null; } }

    internal static AtualizacaoConfig Ler(Assembly assembly)
    {
        return LerRecursos(AtualizacaoLeitorIsolado.Recursos(assembly));
    }

    internal static AtualizacaoConfig LerRecursos(byte[][] recursos)
    {
        if ((recursos[0] == null) == (recursos[1] == null)) throw new InvalidDataException();
        AtualizacaoConfig config;
        if (recursos[0] != null)
        {
            using (var stream = new MemoryStream(recursos[0])) config = Ler(stream);
        }
        else
        {
            long edicao;
            var valor = Encoding.ASCII.GetString(recursos[1]);
            if (!Regex.IsMatch(valor, @"\A[1-9][0-9]*\z") ||
                !long.TryParse(valor, NumberStyles.None, CultureInfo.InvariantCulture, out edicao))
                throw new InvalidDataException();
            config = new AtualizacaoConfig { Edicao = edicao };
        }
        if (recursos[2] != null) config.Instancia = Encoding.ASCII.GetString(recursos[2]);
        if ((config.Instancia != null || config.ExePublico) &&
            !Regex.IsMatch(config.Instancia ?? "", @"\A[0-9a-f]{64}\z")) throw new InvalidDataException();
        return config;
    }

    internal static AtualizacaoConfig LerCanal(byte[] json, long edicao, string moduloAutenticado)
    {
        if (json == null || json.Length > 8192 || edicao < 1) throw new InvalidDataException();
        using (var stream = new MemoryStream(json))
        {
            var c = (AtualizacaoConfig)new DataContractJsonSerializer(typeof(AtualizacaoConfig)).ReadObject(stream);
            if (c == null || c.Edicao != 0 || c.Modulo != moduloAutenticado) throw new CryptographicException();
            c.Edicao = edicao;
            c.Validar();
            var uri = new Uri(c.Origem);
            bool localDeTeste = false;
#if TESTE_ATUALIZACAO
            localDeTeste = uri.Scheme == "http" && uri.IsLoopback;
#endif
            if (!localDeTeste && (uri.Host != "raw.githubusercontent.com" || Repositorio(uri) == null))
                throw new InvalidDataException();
            return c;
        }
    }

    internal static AtualizacaoConfig Ler(Stream s)
    {
        if (s == null || s.Length > 8192) throw new InvalidDataException();
        var c = (AtualizacaoConfig)new DataContractJsonSerializer(typeof(AtualizacaoConfig)).ReadObject(s);
        if (c == null) throw new InvalidDataException();
        c.Validar();
        return c;
    }

    private void Validar()
    {
        if (Edicao < 1 || Convert.FromBase64String(Chave).Length != 32 ||
            Convert.FromBase64String(Modulo).Length != 384 || Expoente != "AQAB")
            throw new InvalidDataException();
        Uri u = new Uri(Origem);
        if (!u.IsAbsoluteUri || !string.IsNullOrEmpty(u.UserInfo) || !string.IsNullOrEmpty(u.Query) ||
            !string.IsNullOrEmpty(u.Fragment) || !(u.Scheme == "https" || (u.Scheme == "http" && u.IsLoopback)))
            throw new InvalidDataException();
    }

    internal bool MesmoCanal(AtualizacaoConfig outro)
    {
        return !ExePublico && !outro.ExePublico && Modulo == outro.Modulo &&
            Expoente == outro.Expoente && Chave == outro.Chave;
    }

    internal static string Repositorio(Uri feed)
    {
        if (feed.Scheme != "https" || !feed.IsDefaultPort) return null;
        const string repositorio = @"\A/([a-zA-Z0-9_.-]+/[a-zA-Z0-9_.-]+)";
        string caminho;
        if (feed.Host == "raw.githubusercontent.com") caminho = @"/refs/heads/canal-publico/manifesto\.txt\z";
        else if (feed.Host == "github.com") caminho = @"/releases/latest/download/manifesto\.txt\z";
        else return null;
        var match = Regex.Match(feed.AbsolutePath, repositorio + caminho);
        return match.Success ? match.Groups[1].Value : null;
    }
}

public sealed class AtualizacaoLeitorIsolado : MarshalByRefObject
{
    public byte[][] LerRecursos(string exe)
    {
        return Recursos(Assembly.ReflectionOnlyLoad(File.ReadAllBytes(exe)));
    }

    internal static byte[][] Recursos(Assembly assembly)
    {
        return new[] { Recurso(assembly, "atualizacao-mix"), Recurso(assembly, "edicao-mix"), Recurso(assembly, "instancia-mix") };
    }

    private static byte[] Recurso(Assembly assembly, string nome)
    {
        using (Stream resource = assembly.GetManifestResourceStream(nome))
        using (var copy = new MemoryStream())
        {
            if (resource == null) return null;
            if (resource.Length > 8192) throw new InvalidDataException();
            resource.CopyTo(copy);
            return copy.ToArray();
        }
    }
}

internal sealed class AtualizacaoPacote
{
    internal const int Limite = 64 * 1024 * 1024;
    internal static readonly byte[] Magic = Encoding.ASCII.GetBytes("MIXENC01");
    internal long Edicao;
    internal int BytesExe, BytesPacote;
    internal byte[] Iv;
    internal string HashExe, HashPacote;

    internal static AtualizacaoPacote Validar(byte[] dados, AtualizacaoConfig config)
    {
        if (dados.Length > 4096) throw new InvalidDataException();
        string texto = new UTF8Encoding(false, true).GetString(dados);
        string[] p = texto.Split('\n');
        if (p.Length != 9 || p[0] != "100NOME-UPD-1" || p[8] != "" || texto.IndexOf('\r') >= 0)
            throw new InvalidDataException();
        byte[] assinado = Encoding.UTF8.GetBytes(string.Join("\n", p, 0, 7) + "\n");
        var provedor = new CspParameters(24);
        provedor.Flags = CspProviderFlags.CreateEphemeralKey;
        using (var rsa = new RSACryptoServiceProvider(provedor))
        {
            rsa.PersistKeyInCsp = false;
            rsa.ImportParameters(new RSAParameters { Modulus = Convert.FromBase64String(config.Modulo),
                Exponent = Convert.FromBase64String(config.Expoente) });
            if (!rsa.VerifyData(assinado, CryptoConfig.MapNameToOID("SHA256"), Convert.FromBase64String(p[7])))
                throw new CryptographicException();
        }
        var m = new AtualizacaoPacote();
        if (!long.TryParse(p[1], NumberStyles.None, CultureInfo.InvariantCulture, out m.Edicao) || m.Edicao < 1 ||
            !int.TryParse(p[2], NumberStyles.None, CultureInfo.InvariantCulture, out m.BytesExe) ||
            !int.TryParse(p[3], NumberStyles.None, CultureInfo.InvariantCulture, out m.BytesPacote) ||
            m.BytesExe < 1 || m.BytesExe > Limite || m.BytesPacote != 8 + (m.BytesExe / 16 + 1) * 16 ||
            !Regex.IsMatch(p[5], "\\A[0-9a-f]{64}\\z") || !Regex.IsMatch(p[6], "\\A[0-9a-f]{64}\\z"))
            throw new InvalidDataException();
        m.Iv = Convert.FromBase64String(p[4]);
        if (m.Iv.Length != 16) throw new InvalidDataException();
        m.HashExe = p[5]; m.HashPacote = p[6];
        return m;
    }

    internal Uri Endereco(AtualizacaoConfig config)
    {
        var feed = new Uri(config.Origem);
        string nome = "pacote-" + HashPacote + ".bin";
        if (feed.Host == "github.com" || feed.Host == "raw.githubusercontent.com")
        {
            var repo = AtualizacaoConfig.Repositorio(feed);
            if (repo == null) throw new InvalidDataException();
            return new Uri("https://github.com/" + repo + "/releases/download/edicao-" +
                Edicao.ToString(CultureInfo.InvariantCulture) + "/" + nome);
        }
        return new Uri(feed, nome);
    }

    internal static string Hash(Stream input)
    {
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
    }

    internal static string HashArquivo(string path)
    {
        using (var file = File.OpenRead(path)) return Hash(file);
    }

    internal void Abrir(string pacote, string destino, AtualizacaoConfig config)
    {
        using (var input = File.OpenRead(pacote))
        using (var aes = Aes.Create())
        {
            if (input.Length != BytesPacote || Hash(input) != HashPacote) throw new CryptographicException();
            input.Position = 0;
            for (int i = 0; i < Magic.Length; i++) if (input.ReadByte() != Magic[i]) throw new InvalidDataException();
            aes.Key = Convert.FromBase64String(config.Chave);
            aes.IV = Iv; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
            using (var dec = aes.CreateDecryptor())
            using (var clear = new CryptoStream(input, dec, CryptoStreamMode.Read))
            using (var output = new FileStream(destino, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                clear.CopyTo(output);
        }
        if (new FileInfo(destino).Length != BytesExe || HashArquivo(destino) != HashExe)
            throw new CryptographicException();
    }

    internal static void Baixar(Uri url, Stream output, int maximo, CancellationToken cancelamento)
    {
        Uri origem = url;
        if (url.Scheme == "https" && ServicePointManager.SecurityProtocol != SecurityProtocolType.SystemDefault)
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        for (int salto = 0; salto < 5; salto++)
        {
            cancelamento.ThrowIfCancellationRequested();
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET"; req.AllowAutoRedirect = false;
            req.UserAgent = "100Nome-Launcher/1"; req.Timeout = 7500; req.ReadWriteTimeout = 7500;
            req.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
            using (cancelamento.Register(req.Abort))
            using (var res = (HttpWebResponse)req.GetResponse())
            {
                int code = (int)res.StatusCode;
                if (code >= 300 && code < 400)
                {
                    string loc = res.Headers["Location"];
                    if (string.IsNullOrEmpty(loc)) throw new InvalidDataException();
                    var next = new Uri(url, loc);
                    bool github = origem.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
                        next.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);
                    bool mesmaOrigem = next.Scheme == origem.Scheme && next.Authority.Equals(origem.Authority,
                        StringComparison.OrdinalIgnoreCase);
                    if ((!mesmaOrigem && !(github && next.Scheme == "https" && next.IsDefaultPort)) ||
                        !string.IsNullOrEmpty(next.UserInfo)) throw new InvalidDataException();
                    url = next; continue;
                }
                if (code != 200 || res.ContentLength > maximo) throw new InvalidDataException();
                using (var input = res.GetResponseStream())
                {
                    byte[] buf = new byte[65536];
                    int total = 0, n;
                    while ((n = input.Read(buf, 0, buf.Length)) > 0)
                    {
                        cancelamento.ThrowIfCancellationRequested();
                        if (n > maximo - total) throw new InvalidDataException();
                        output.Write(buf, 0, n); total += n;
                    }
                    if (res.ContentLength >= 0 && res.ContentLength != total) throw new EndOfStreamException();
                    return;
                }
            }
        }
        throw new InvalidDataException();
    }
}
