using System;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

internal static class AutorizacaoRota
{
    private static int precisaVinculo;
    internal static bool PrecisaVinculo { get { return Volatile.Read(ref precisaVinculo) != 0; } }

    [DataContract]
    private sealed class Desafio
    {
        [DataMember(Name = "desafio", IsRequired = true)] public string Valor { get; set; }
        [DataMember(Name = "vinculo")] public bool Vinculo { get; set; }
    }

    [DataContract]
    private sealed class Autorizacao
    {
        [DataMember(Name = "ticket", IsRequired = true)] public string Ticket { get; set; }
        [DataMember(Name = "validadeSegundos", IsRequired = true)] public int ValidadeSegundos { get; set; }
    }

    [DataContract]
    private sealed class RespostaConvite
    {
        [DataMember(Name = "senha", IsRequired = true)] public string Senha { get; set; }
    }

    internal static string Finalidade(UsoEntrada uso)
    {
        if (uso == UsoEntrada.Medir) return "medir";
        if (uso == UsoEntrada.Jogar) return "jogar";
        throw new ArgumentOutOfRangeException("uso");
    }

    internal static string ObterTicket(string rota, UsoEntrada uso, CancellationToken token)
    {
        if (PrecisaVinculo) throw new InvalidDataException("Este PC precisa liberar a Entrada 2.");
        return Solicitar(rota, uso, false, token);
    }

    internal static void VincularComConsentimento(CancellationToken token)
    {
        // Um desafio novo evita aproveitar um aceite anterior ou um desafio já vencido.
        Solicitar("ws", UsoEntrada.Medir, true, token);
    }

    internal static string ObterSenhaDoJogo(bool consentiu, CancellationToken token)
    {
        // Uma consulta no clique confere também a revogação da licença.
        return Solicitar("convite", UsoEntrada.Jogar, consentiu, token);
    }

    private static string Solicitar(string rota, UsoEntrada uso, bool consentiu, CancellationToken token)
    {
        using (var rsa = LicencaPC.AbrirChave())
        {
            string codigo = LicencaPC.CalcularCodigo(LicencaPC.GuidDaInstalacao(), rsa);
            string licenca = LicencaPC.LerCredencial(codigo);
            foreach (string host in CadastroAutomatico.Destinos())
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    return Provar(rota, uso, codigo, rsa, consentiu, LicencaPC.GuidDaInstalacao,
                        (caminho, json) => Post(host, caminho, json, licenca, token));
                }
                catch (WebException erro)
                {
                    token.ThrowIfCancellationRequested();
                    // Só tente outro endereço se nenhum serviço respondeu a esta conexão.
                    if (erro.Response != null) { erro.Response.Close(); throw; }
                }
            }
        }
        throw new IOException("A central não respondeu ao pedido de autorização.");
    }

    // Os valores variáveis dos corpos JSON têm alfabeto limitado (base64, GUID ou palavras do protocolo).
    internal static string Provar(string rota, UsoEntrada uso, string codigo, RSACryptoServiceProvider rsa,
        bool consentiu, Func<string> lerInstalacao, Func<string,string,Tuple<int,byte[]>> enviar)
    {
        bool convite = rota == "convite" && uso == UsoEntrada.Jogar;
        if (!convite && rota != "ws" && rota != "udp") throw new ArgumentOutOfRangeException("rota");
        string finalidade = Finalidade(uso);
        var publica = rsa.ExportParameters(false);
        if (publica.Modulus.Length != 256 || Convert.ToBase64String(publica.Exponent) != "AQAB")
            throw new CryptographicException("Chave do PC incompatível.");
        string pedido = "{\"rota\":\"" + rota + "\",\"finalidade\":\"" + finalidade +
            "\",\"modulo\":\"" + Convert.ToBase64String(publica.Modulus) + "\"}";
        var primeira = enviar("/v3/rota/desafio", pedido);
        if (convite && primeira.Item1 == 403) throw new UnauthorizedAccessException();
        if (primeira.Item1 != 200 && primeira.Item1 != 409)
            throw new IOException("A central recusou o desafio de acesso.");
        var desafio = Ler<Desafio>(primeira.Item2);
        if (!Regex.IsMatch(desafio.Valor ?? "", @"\A[A-Za-z0-9_-]{43}\z") || desafio.Vinculo != (primeira.Item1 == 409))
            throw new InvalidDataException("Desafio de acesso inválido.");
        if (desafio.Vinculo && !consentiu)
        {
            Volatile.Write(ref precisaVinculo, 1);
            if (convite) return null;
            throw new InvalidDataException("Este PC precisa liberar a Entrada 2.");
        }
        string texto = "100NOME-PC-PROVA-1\n" + codigo + "\n" + rota + "\n" + finalidade + "\n" + desafio.Valor + "\n";
        string assinatura = Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(texto), "SHA256"));
        string prova = "{\"desafio\":\"" + desafio.Valor + "\",\"assinatura\":\"" + assinatura + "\"";
        if (desafio.Vinculo)
        {
            string instalacao = lerInstalacao();
            if (!Regex.IsMatch(instalacao ?? "", @"\A[0-9a-f]{32}\z"))
                throw new InvalidDataException("Identidade da instalação inválida.");
            prova += ",\"instalacao\":\"" + instalacao + "\"";
        }
        var segunda = enviar(convite ? "/v3/convite" : "/v3/rota/ticket", prova + "}");
        if (convite && segunda.Item1 == 403) throw new UnauthorizedAccessException();
        if (segunda.Item1 != 200) throw new IOException("A central recusou a autorização.");
        if (convite)
        {
            string senha = Ler<RespostaConvite>(segunda.Item2).Senha;
            if (!Convite.SenhaValida(senha)) throw new InvalidDataException("Senha do convite inválida.");
            Volatile.Write(ref precisaVinculo, 0);
            return senha;
        }
        var autorizacao = Ler<Autorizacao>(segunda.Item2);
        if (autorizacao.ValidadeSegundos <= 0 || autorizacao.ValidadeSegundos > 90 ||
            autorizacao.Ticket == null || autorizacao.Ticket.Length > 1017 ||
            !Regex.IsMatch(autorizacao.Ticket, @"\A[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\z"))
            throw new InvalidDataException("Autorização da Entrada 2 inválida.");
        Volatile.Write(ref precisaVinculo, 0);
        return autorizacao.Ticket;
    }

    private static T Ler<T>(byte[] dados) where T : class
    {
        try
        {
            using (var stream = new MemoryStream(dados))
            {
                var resposta = (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
                if (resposta == null) throw new InvalidDataException("Resposta de autorização vazia.");
                return resposta;
            }
        }
        catch (SerializationException erro) { throw new InvalidDataException("Resposta de autorização inválida.", erro); }
    }

    private static Tuple<int,byte[]> Post(string host, string caminho, string json, string licenca, CancellationToken token)
    {
        var req = (HttpWebRequest)WebRequest.Create(CadastroAutomatico.Endereco(host, caminho));
        req.Method = "POST";
        req.Proxy = null;
        req.AllowAutoRedirect = false;
        req.Timeout = req.ReadWriteTimeout = 5000;
        req.ContentType = "application/json";
        req.ServerCertificateValidationCallback = CadastroAutomatico.Certificado;
        req.Headers["X-100Nome-Licenca"] = licenca;
        byte[] corpo = Encoding.UTF8.GetBytes(json);
        req.ContentLength = corpo.Length;
        using (token.Register(req.Abort))
        {
            using (var escrita = req.GetRequestStream()) escrita.Write(corpo, 0, corpo.Length);
            HttpWebResponse resposta;
            try { resposta = (HttpWebResponse)req.GetResponse(); }
            catch (WebException erro)
            {
                resposta = erro.Response as HttpWebResponse;
                if (resposta == null) throw;
            }
            using (resposta)
            using (var leitor = new BinaryReader(resposta.GetResponseStream()))
            {
                var bytes = leitor.ReadBytes(4097);
                if (bytes.Length > 4096) throw new InvalidDataException("Resposta de autorização muito longa.");
                return Tuple.Create((int)resposta.StatusCode, bytes);
            }
        }
    }
}
