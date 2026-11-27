using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

internal static class PatentePessoal
{
    [DataContract]
    private sealed class Resposta
    {
        [DataMember(Name="faixa")] public int Faixa { get; set; }
    }

    internal static Task<PatenteDoMix> ConsultarAsync(ulong steam, CancellationToken token)
    {
        return Task.Run(() => {
            foreach (string destino in CadastroAutomatico.Destinos())
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    return Consultar(destino, steam, token);
                }
                catch (WebException ex)
                {
                    token.ThrowIfCancellationRequested();
                    using (var resposta = ex.Response as HttpWebResponse)
                    {
                        if (resposta != null) return null;
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { break; }
                catch (SerializationException) { break; }
            }
            return null;
        }, token);
    }

    private static PatenteDoMix Consultar(string host, ulong steam, CancellationToken token)
    {
        var req = (HttpWebRequest)WebRequest.Create(CadastroAutomatico.Endereco(host, "/v2/patentes"));
        req.Method = "POST";
        req.Proxy = null;
        req.AllowAutoRedirect = false;
        req.Timeout = req.ReadWriteTimeout = 4000;
        req.ContentType = "application/json";
        req.ServerCertificateValidationCallback = CadastroAutomatico.Certificado;
        req.Headers["X-100Nome-Licenca"] = LicencaPC.CredencialDaEntrada();
        byte[] corpo = Encoding.ASCII.GetBytes("{\"steamId\":\"" + steam.ToString(CultureInfo.InvariantCulture) + "\"}");
        req.ContentLength = corpo.Length;
        using (token.Register(req.Abort))
        {
            using (var escrita = req.GetRequestStream()) escrita.Write(corpo, 0, corpo.Length);
            using (var resposta = (HttpWebResponse)req.GetResponse())
            using (var leitor = new BinaryReader(resposta.GetResponseStream()))
                return Interpretar(leitor.ReadBytes(257));
        }
    }

    internal static PatenteDoMix Interpretar(byte[] dados)
    {
        if (dados == null || dados.Length == 0 || dados.Length > 256) return null;
        using (var stream = new MemoryStream(dados))
        {
            var r = (Resposta)new DataContractJsonSerializer(typeof(Resposta)).ReadObject(stream);
            if (r == null || r.Faixa < 1 || r.Faixa > PatenteDoMix.Catalogo.Length) return null;
            return PatenteDoMix.Catalogo[r.Faixa - 1];
        }
    }
}
