using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

internal sealed class NoticiaSteam
{
    public string Titulo { get; private set; }
    public DateTimeOffset Publicado { get; private set; }
    public Uri Link { get; private set; }
    internal NoticiaSteam(string titulo, DateTimeOffset publicado, Uri link)
    { Titulo = titulo; Publicado = publicado; Link = link; }
}

internal sealed class BoletimSteam
{
    public NoticiaSteam[] Itens { get; private set; }
    public bool EmCache { get; private set; }
    internal BoletimSteam(NoticiaSteam[] itens, bool emCache) { Itens = itens; EmCache = emCache; }
}

internal static class NoticiasSteam
{
    private static readonly Uri Feed = new Uri("https://store.steampowered.com/feeds/news/app/730/?l=brazilian");
    private const int Limite = 1024 * 1024;
    private static readonly string Cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        PerfilInstancia.PastaDados, "noticias-cs2.rss");

    internal static Task<BoletimSteam> ConsultarAsync(CancellationToken token)
    {
        return Task.Run(() => Consultar(token), token);
    }

    private static BoletimSteam Consultar(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        NoticiaSteam[] anteriores = new NoticiaSteam[0];
        try
        {
            using (var local = new FileStream(Cache, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                anteriores = Ler(local);
            var idade = DateTime.UtcNow - File.GetLastWriteTimeUtc(Cache);
            if (idade >= TimeSpan.Zero && idade < TimeSpan.FromMinutes(30))
                return new BoletimSteam(anteriores, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (XmlException) { }
        catch (InvalidDataException) { }

        try
        {
            using (var dados = new MemoryStream())
            {
                AtualizacaoPacote.Baixar(Feed, dados, Limite, token);
                token.ThrowIfCancellationRequested();
                dados.Position = 0;
                var itens = Ler(dados);
                try { GravarCache(dados); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                return new BoletimSteam(itens, false);
            }
        }
        catch (WebException) { token.ThrowIfCancellationRequested(); }
        catch (IOException) { }
        catch (InvalidDataException) { }
        catch (XmlException) { }
        return new BoletimSteam(anteriores, true);
    }

    internal static NoticiaSteam[] Ler(Stream fonte)
    {
        if (fonte.Length > Limite) throw new InvalidDataException();
        var xml = new XmlDocument { XmlResolver = null };
        using (var leitor = XmlReader.Create(fonte, new XmlReaderSettings {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = Limite }))
            xml.Load(leitor);
        var noticias = new List<NoticiaSteam>();
        var links = new HashSet<string>(StringComparer.Ordinal);
        foreach (XmlElement item in xml.SelectNodes("/rss/channel/item"))
        {
            var titulo = item["title"];
            var endereco = item["link"];
            var data = item["pubDate"];
            Uri link;
            DateTimeOffset quando;
            if (titulo == null || data == null || endereco == null || !Uri.TryCreate(endereco.InnerText, UriKind.Absolute, out link) ||
                link.Scheme != Uri.UriSchemeHttps || !link.IsDefaultPort || link.Host != "store.steampowered.com" ||
                link.UserInfo.Length != 0 || !link.AbsolutePath.StartsWith("/news/app/730/view/", StringComparison.Ordinal) ||
                !DateTimeOffset.TryParse(data.InnerText, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out quando)) continue;
            string nome = titulo.InnerText.Trim();
            if (nome.Length == 0 || nome.Length > 240 || nome.Any(char.IsControl) || !links.Add(link.AbsoluteUri)) continue;
            noticias.Add(new NoticiaSteam(nome, quando, link));
        }
        if (noticias.Count == 0) throw new InvalidDataException("O boletim da Steam não contém notícias válidas.");
        return noticias.OrderByDescending(n => n.Publicado).Take(6).ToArray();
    }

    private static void GravarCache(MemoryStream dados)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Cache));
        string temp = Cache + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var arquivo = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) dados.WriteTo(arquivo);
            if (File.Exists(Cache)) File.Replace(temp, Cache, null); else File.Move(temp, Cache);
        }
        finally { File.Delete(temp); }
    }
}
