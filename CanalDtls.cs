using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;

// Cada sessão usa um socket conectado exclusivamente ao par autenticado.
internal sealed class CanalDtls : CanalUdp
{
    private readonly DtlsTransport dtls;
    private readonly Socket socket;
    private int fechado;

    internal CanalDtls(Socket socket, string id, byte[] chave)
    {
        this.socket = socket;
        dtls = new DtlsClientProtocol().Connect(new Cliente(id, chave), new Datagramas(socket));
    }

    internal override void Enviar(byte[] dados, int tamanho) { dtls.Send(dados, 0, tamanho); }
    internal override int Receber(byte[] dados, int prazoMs) { return dtls.Receive(dados, 0, dados.Length, prazoMs); }
    public override void Dispose()
    {
        if (Interlocked.Exchange(ref fechado, 1) != 0) return;
        try { dtls.Close(); }
        catch (Exception erro) { if (!ErroDeRede(erro)) throw; }
        finally { socket.Close(); }
    }

    private sealed class Cliente : PskTlsClient
    {
        internal Cliente(string id, byte[] chave) : base(new BcTlsCrypto(), Encoding.ASCII.GetBytes(id), chave) { }
        protected override ProtocolVersion[] GetSupportedVersions() { return ProtocolVersion.DTLSv12.Only(); }
        protected override int[] GetSupportedCipherSuites() { return new[] { CipherSuite.TLS_PSK_WITH_AES_128_GCM_SHA256 }; }
        public override int GetHandshakeTimeoutMillis() { return 7000; }
        public override int GetHandshakeResendTimeMillis() { return 400; }
    }

    private sealed class Datagramas : DatagramTransport
    {
        private readonly Socket socket;
        internal Datagramas(Socket socket) { this.socket = socket; }
        public int GetReceiveLimit() { return TamanhoMaximo + 512; }
        public int GetSendLimit() { return TamanhoMaximo + 512; }
        public void Close() { socket.Close(); }
        public void Send(byte[] dados, int pos, int tamanho) { socket.Send(dados, pos, tamanho, SocketFlags.None); }
        public int Receive(byte[] dados, int pos, int tamanho, int prazoMs)
        {
            // Poll limita a espera sem alterar o socket usado simultaneamente na ida.
            if (!socket.Poll(1000 * Math.Max(1, Math.Min(prazoMs, 7000)), SelectMode.SelectRead)) return -1;
            return socket.Receive(dados, pos, tamanho, SocketFlags.None);
        }
    }
}
