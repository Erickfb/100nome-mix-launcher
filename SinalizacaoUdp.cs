using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

internal static class SinalizacaoUdp
{
    [DataContract]
    internal sealed class Sessao
    {
        [DataMember(Name = "ip", IsRequired = true)] public string Ip { get; set; }
        [DataMember(Name = "porta", IsRequired = true)] public int Porta { get; set; }
        [DataMember(Name = "id", IsRequired = true)] public string Id { get; set; }
        [DataMember(Name = "chave", IsRequired = true)] public string Chave { get; set; }
    }

    internal static IPEndPoint Publico(IPAddress ip, int porta)
    {
        if (ip == null || ip.AddressFamily != AddressFamily.InterNetwork || porta < 1 || porta > 65535)
            throw new InvalidDataException("Endereço UDP inválido.");
        var b = ip.GetAddressBytes();
        if (b[0] == 0 || b[0] == 10 || b[0] == 127 || b[0] >= 224 ||
            (b[0] == 100 && b[1] >= 64 && b[1] <= 127) || (b[0] == 169 && b[1] == 254) ||
            (b[0] == 172 && b[1] >= 16 && b[1] <= 31) ||
            (b[0] == 192 && (b[1] == 168 || (b[1] == 0 && (b[2] == 0 || b[2] == 2)) || (b[1] == 88 && b[2] == 99))) ||
            (b[0] == 198 && (b[1] == 18 || b[1] == 19 || (b[1] == 51 && b[2] == 100))) ||
            (b[0] == 203 && b[1] == 0 && b[2] == 113)) throw new InvalidDataException("A entrada precisa de um IPv4 público.");
        return new IPEndPoint(ip, porta);
    }

    // RFC 8489. O endereço observado só vale para a mesma porta usada no DTLS.
    internal static IPEndPoint Descobrir(Socket socket, CancellationToken token)
    {
        var ips = Dns.GetHostAddressesAsync("stun.cloudflare.com");
        if (System.Threading.Tasks.Task.WhenAny(ips, System.Threading.Tasks.Task.Delay(2000, token)).GetAwaiter().GetResult() != ips)
            throw new IOException("O STUN demorou para resolver.");
        var servidor = Publico(ips.GetAwaiter().GetResult().FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork), 3478);
        var pedido = new byte[20];
        using (var aleatorio = RandomNumberGenerator.Create()) aleatorio.GetBytes(pedido);
        pedido[0] = 0; pedido[1] = 1; pedido[2] = pedido[3] = 0;
        pedido[4] = 0x21; pedido[5] = 0x12; pedido[6] = 0xa4; pedido[7] = 0x42;
        var resposta = new byte[2048];
        for (int tentativa = 0; tentativa < 3; tentativa++)
        {
            token.ThrowIfCancellationRequested();
            socket.SendTo(pedido, servidor);
            var relogio = Stopwatch.StartNew();
            int prazo = 350 << tentativa;
            while (relogio.ElapsedMilliseconds < prazo)
            {
                if (!socket.Poll(Math.Max(1, prazo - (int)relogio.ElapsedMilliseconds) * 1000, SelectMode.SelectRead)) break;
                EndPoint origem = new IPEndPoint(IPAddress.Any, 0);
                int tamanho = socket.ReceiveFrom(resposta, ref origem);
                if (!servidor.Equals(origem)) continue;
                var observado = LerStun(pedido, resposta, tamanho);
                if (observado != null) return observado;
            }
        }
        throw new IOException("O STUN não respondeu.");
    }

    internal static IPEndPoint LerStun(byte[] pedido, byte[] resposta, int tamanho)
    {
        if (tamanho < 20 || resposta[0] != 1 || resposta[1] != 1 ||
            20 + (resposta[2] << 8) + resposta[3] != tamanho) return null;
        for (int i = 4; i < 20; i++) if (resposta[i] != pedido[i]) return null;
        for (int pos = 20; pos + 4 <= tamanho;)
        {
            int tipo = (resposta[pos] << 8) | resposta[pos + 1];
            int n = (resposta[pos + 2] << 8) | resposta[pos + 3];
            pos += 4;
            if (n > tamanho - pos) return null;
            if (tipo == 0x20 && n == 8 && resposta[pos + 1] == 1)
            {
                var bytes = new byte[4];
                for (int i = 0; i < 4; i++) bytes[i] = (byte)(resposta[pos + 4 + i] ^ pedido[4 + i]);
                int porta = ((resposta[pos + 2] << 8) | resposta[pos + 3]) ^ 0x2112;
                return Publico(new IPAddress(bytes), porta);
            }
            pos += (n + 3) & ~3;
        }
        return null;
    }

    internal static Sessao Negociar(IPEndPoint local, UsoEntrada uso, CancellationToken token)
    {
        var ticket = AutorizacaoRota.ObterTicket("udp", uso, token);
        var req = (HttpWebRequest)WebRequest.Create(CadastroAutomatico.Endereco(Convite.Dominio, "/v3/udp/" + AutorizacaoRota.Finalidade(uso)));
        req.Method = "POST";
        req.AllowAutoRedirect = false;
        req.Proxy = null;
        req.Timeout = req.ReadWriteTimeout = 6000;
        req.ContentType = "application/json";
        req.ServerCertificateValidationCallback = CadastroAutomatico.Certificado;
        req.Headers["X-100Nome-Licenca"] = LicencaPC.CredencialDaEntrada();
        req.Headers[HttpRequestHeader.Authorization] = "Bearer " + ticket;
        byte[] corpo = Encoding.ASCII.GetBytes(String.Format(System.Globalization.CultureInfo.InvariantCulture,
            "{{\"ip\":\"{0}\",\"porta\":{1}}}", local.Address, local.Port));
        req.ContentLength = corpo.Length;
        using (token.Register(req.Abort))
        {
            using (var escrita = req.GetRequestStream()) escrita.Write(corpo, 0, corpo.Length);
            try
            {
                using (var resposta = (HttpWebResponse)req.GetResponse())
                using (var leitor = new BinaryReader(resposta.GetResponseStream()))
                {
                    if ((int)resposta.StatusCode < 200 || (int)resposta.StatusCode >= 300) throw new IOException("Entrada indisponível.");
                    var dados = leitor.ReadBytes(1025);
                    if (dados.Length > 1024) throw new InvalidDataException("Sessão UDP inválida.");
                    using (var stream = new MemoryStream(dados))
                    {
                        var sessao = (Sessao)new DataContractJsonSerializer(typeof(Sessao)).ReadObject(stream);
                        if (sessao == null || sessao.Ip == null || !Regex.IsMatch(sessao.Id ?? "", @"\A[0-9a-f]{32}\z") ||
                            !Regex.IsMatch(sessao.Chave ?? "", @"\A[A-Za-z0-9+/]{43}=\z"))
                            throw new InvalidDataException("Sessão UDP inválida.");
                        return sessao;
                    }
                }
            }
            catch (WebException erro)
            {
                if (erro.Response != null) erro.Response.Close();
                throw;
            }
            catch (SerializationException erro) { throw new InvalidDataException("Sessão UDP ilegível.", erro); }
        }
    }
}
