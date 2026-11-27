using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

internal static class CadastroAutomatico
{
    static CadastroAutomatico()
    {
        // O executável pode iniciar com o padrão antigo do .NET Framework.
        // A primeira consulta do perfil também precisa negociar TLS moderno.
        if (ServicePointManager.SecurityProtocol != SecurityProtocolType.SystemDefault)
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
    }

    internal static Uri Endereco(string host, string caminho)
    {
        return new UriBuilder(Uri.UriSchemeHttps, host, PerfilInstancia.PortaCadastro, caminho).Uri;
    }

    private static readonly byte[] EnderecoLocal = string.IsNullOrEmpty(PerfilInstancia.EnderecoLocalCadastro)
        ? null : IPAddress.Parse(PerfilInstancia.EnderecoLocalCadastro).GetAddressBytes();
    internal enum Resultado { Autorizado, Fechado, Revogado, Ausente, Indisponivel, MuitasTentativas }

    [DataContract]
    private sealed class Pedido
    {
        [DataMember(Name="codigo")] public string Codigo;
        [DataMember(Name="nome", EmitDefaultValue=false)] public string Nome;
    }

    internal static bool Certificado(object origem, X509Certificate certificado, X509Chain cadeia, SslPolicyErrors erros)
    {
        if (certificado == null || (erros & ~SslPolicyErrors.RemoteCertificateChainErrors) != 0) return false;
        using (var folha = new X509Certificate2(certificado))
        using (var hash = SHA256.Create())
            return DateTime.Now >= folha.NotBefore && DateTime.Now <= folha.NotAfter &&
                Convert.ToBase64String(hash.ComputeHash(folha.GetPublicKey())) == PerfilInstancia.PinTlsCadastro;
    }

    private static bool NaRedeLocal()
    {
        if (EnderecoLocal == null) return false;
        foreach (var rede in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (rede.OperationalStatus != OperationalStatus.Up) continue;
            foreach (var item in rede.GetIPProperties().UnicastAddresses)
            {
                var b = item.Address.GetAddressBytes();
                if (b.Length == 4 && b[0] == EnderecoLocal[0] && b[1] == EnderecoLocal[1] && b[2] == EnderecoLocal[2]) return true;
            }
        }
        return false;
    }

    internal static List<string> Destinos()
    {
        var destinos = new List<string>();
        try { if (NaRedeLocal()) destinos.Add(PerfilInstancia.EnderecoLocalCadastro); } catch (NetworkInformationException) { }
#if PERFIL_REMOTO
        destinos.Add(PerfilInstancia.HostCadastro);
#else
        destinos.Add(Convite.Dominio);
#endif
        return destinos;
    }

    internal static ulong ContaSteamAtiva()
    {
        try
        {
            using (var chave = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess"))
            {
                object valor = chave == null ? null : chave.GetValue("ActiveUser");
                if (valor is int && (int)valor != 0) return 76561197960265728UL + unchecked((uint)(int)valor);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (System.Security.SecurityException) { }
        return 0;
    }

    // O apelido público da conta ativa identifica o PC na lista de convites.
    // O login da conta Steam e os nomes do usuário e do computador Windows ficam fora do pedido.
    private static string ApelidoSteam()
    {
        try
        {
            using (var chave = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
            {
                string pasta = chave == null ? null : chave.GetValue("SteamPath") as string;
                ulong id = ContaSteamAtiva();
                if (String.IsNullOrEmpty(pasta) || id == 0) return "";
                string arquivo = Path.Combine(pasta,"config","loginusers.vdf");
                using (var f = new FileStream(arquivo,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
                using (var leitor = new StreamReader(f,Encoding.UTF8,true))
                {
                    if (f.Length > 1048576) return "";
                    bool conta = false;
                    for (string linha; (linha = leitor.ReadLine()) != null;)
                    {
                        linha = linha.Trim();
                        if (linha == "\"" + id.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\"") conta = true;
                        else if (conta && linha == "}") break;
                        else if (conta && linha.StartsWith("\"PersonaName\"",StringComparison.OrdinalIgnoreCase))
                        {
                            var valorNome = linha.Substring("\"PersonaName\"".Length).Trim();
                            if (valorNome.Length < 2 || valorNome[0] != '"' || valorNome[valorNome.Length-1] != '"') return "";
                            string apelido = valorNome.Substring(1,valorNome.Length-2).Replace("\\\"","\"").Replace("\\\\","\\").Trim();
                            if (apelido.Length > 60 || Array.Exists(apelido.ToCharArray(), Char.IsControl)) return "";
                            return apelido;
                        }
                    }
                }
            }
        }
        catch (IOException) { } catch (UnauthorizedAccessException) { } catch (System.Security.SecurityException) { }
        return "";
    }

    internal static Task<Resultado> ConferirAcesso(string codigo, CancellationToken token)
    {
        return Task.Run(() => {
            var destinos = Destinos();
            if (LicencaPC.Autorizado(codigo))
            {
                var estado = Solicitar(destinos, codigo, false, token);
                if (estado != Resultado.Ausente) return estado;
            }
            var cadastro = Solicitar(destinos, codigo, true, token);
            if (cadastro != Resultado.Autorizado) return cadastro;
            return Solicitar(destinos, codigo, false, token);
        }, token);
    }

    private static Resultado Solicitar(List<string> destinos, string codigo, bool cadastrar, CancellationToken token)
    {
        byte[] dados;
        using (var stream = new MemoryStream())
        {
            var pedido = new Pedido { Codigo = codigo, Nome = cadastrar ? ApelidoSteam() : null };
            new DataContractJsonSerializer(typeof(Pedido)).WriteObject(stream, pedido);
            dados = stream.ToArray();
        }

        string caminho = cadastrar ? "/v1/cadastro" : "/v1/licenca/validar";
        foreach (string destino in destinos)
        {
            token.ThrowIfCancellationRequested();
            var req = (HttpWebRequest)WebRequest.Create(Endereco(destino, caminho));
            req.Method = "POST"; req.ContentType = "application/json"; req.ContentLength = dados.Length;
            req.AllowAutoRedirect = false; req.Proxy = null; req.Timeout = 6000; req.ReadWriteTimeout = 6000;
            req.ServerCertificateValidationCallback = Certificado;
            try
            {
                using (token.Register(req.Abort))
                {
                    using (var saida = req.GetRequestStream()) saida.Write(dados, 0, dados.Length);
                    using (var resposta = (HttpWebResponse)req.GetResponse())
                    {
                        if (!cadastrar && resposta.StatusCode == HttpStatusCode.NoContent) return Resultado.Autorizado;
                        if (cadastrar && resposta.StatusCode == HttpStatusCode.OK)
                        {
                            using (var leitor = new BinaryReader(resposta.GetResponseStream()))
                            {
                                var licenca = leitor.ReadBytes(2049);
                                token.ThrowIfCancellationRequested();
                                LicencaPC.SalvarAssinada(licenca, codigo);
                            }
                            return Resultado.Autorizado;
                        }
                    }
                }
            }
            catch (WebException erro)
            {
                token.ThrowIfCancellationRequested();
                using (var resposta = erro.Response as HttpWebResponse)
                {
                    if (resposta == null) continue;
                    if (resposta.StatusCode == HttpStatusCode.Forbidden) return cadastrar ? Resultado.Fechado : Resultado.Revogado;
                    if (cadastrar && resposta.StatusCode == HttpStatusCode.Gone) return Resultado.Revogado;
                    if (!cadastrar && resposta.StatusCode == HttpStatusCode.NotFound) return Resultado.Ausente;
                    if ((int)resposta.StatusCode == 429) return Resultado.MuitasTentativas;
                }
            }
        }
        return Resultado.Indisponivel;
    }
}
