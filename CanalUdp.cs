using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

internal abstract class CanalUdp : CanalJogo
{
    internal const int TamanhoMaximo = 8000;
    private static readonly Lazy<Assembly> biblioteca = new Lazy<Assembly>(delegate
    {
        using (var recurso = typeof(CanalUdp).Assembly.GetManifestResourceStream("BouncyCastle.Cryptography.dll"))
        using (var leitor = new BinaryReader(recurso))
            return Assembly.Load(leitor.ReadBytes(checked((int)recurso.Length)));
    });

    static CanalUdp()
    {
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object origem, ResolveEventArgs pedido)
        {
            return pedido.Name == "BouncyCastle.Cryptography, Version=2.0.0.0, Culture=neutral, PublicKeyToken=072edcf4a5328938" &&
                pedido.RequestingAssembly == typeof(CanalUdp).Assembly ? biblioteca.Value : null;
        };
    }

    internal static Task<CanalUdp> AbrirPilotoAsync(UsoEntrada uso, CancellationToken token)
    {
        return Task.Run(delegate { return Negociar(uso, token); }, token);
    }

    // A referência ao tipo que implementa DTLS só é resolvida depois da inicialização acima.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static CanalUdp Negociar(UsoEntrada uso, CancellationToken token)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            socket.ExclusiveAddressUse = true;
            socket.Bind(new IPEndPoint(IPAddress.Any, 0));
            using (token.Register(socket.Close))
            {
                var local = SinalizacaoUdp.Descobrir(socket, token);
                var sessao = SinalizacaoUdp.Negociar(local, uso, token);
                IPAddress remoto;
                if (!IPAddress.TryParse(sessao.Ip, out remoto)) throw new InvalidDataException("Endereço da entrada inválido.");
                socket.Connect(SinalizacaoUdp.Publico(remoto, sessao.Porta));
                var chave = Convert.FromBase64String(sessao.Chave);
                try { return new CanalDtls(socket, sessao.Id, chave); }
                finally { Array.Clear(chave, 0, chave.Length); }
            }
        }
        catch { socket.Close(); throw; }
    }

    internal override Task EnviarAsync(byte[] dados, int tamanho, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (tamanho <= TamanhoMaximo) Enviar(dados, tamanho);
        return Task.FromResult(0);
    }

    internal override Task LerAsync(Action<byte[], int> entregar, CancellationToken token)
    {
        return Task.Factory.StartNew(delegate
        {
            var dados = new byte[TamanhoMaximo];
            while (!token.IsCancellationRequested)
            {
                int n = Receber(dados, 250);
                if (n > 0) entregar(dados, n);
            }
        }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    protected override int Trocar(byte[] pedido, int tamanho, byte[] resposta, CancellationToken token)
    {
        Enviar(pedido, tamanho);
        return Receber(resposta, 900);
    }

    internal abstract void Enviar(byte[] dados, int tamanho);
    internal abstract int Receber(byte[] dados, int prazoMs);
}
