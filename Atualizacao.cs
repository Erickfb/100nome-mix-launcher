using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

internal static class Atualizacao
{
    private static readonly string Cache = Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), PerfilInstancia.PastaDados, "Mix", "atualizacoes");
    private const string Aplicar = "--mix-aplicar", Aplicado = "--mix-atualizado", Restaurado = "--mix-restaurado";
    private static readonly object Guarda = new object();
    private static EventWaitHandle confirmacao;
    private static CancellationToken tokenPendente;
    private static volatile AtualizacaoConfig canalDoPerfil;

    internal static bool GerenciadaPelaStore { get; private set; }
    internal static bool TemCanal { get { return canalDoPerfil != null || TemLegado; } }
    private static readonly bool TemLegado = typeof(Atualizacao).Assembly.GetManifestResourceInfo("atualizacao-mix") != null;

    internal static void ConfigurarCanal(byte[] json)
    {
#if PERFIL_REMOTO || PERFIL_TRANSICAO
        var local = AtualizacaoConfig.Ler(typeof(Atualizacao).Assembly);
        var canal = AtualizacaoConfig.LerCanal(json, local.Edicao, PerfilRemoto.ModuloAtualizador());
        if (local.Instancia == null || (!local.ExePublico && local.Chave == canal.Chave))
            throw new CryptographicException();
        canalDoPerfil = canal;
#else
        throw new InvalidOperationException();
#endif
    }

    internal static Uri NotasDaVersao
    {
        get {
            if (GerenciadaPelaStore || !TemCanal) return null;
            var config = canalDoPerfil ?? AtualizacaoConfig.Ler(typeof(Atualizacao).Assembly);
            var repo = AtualizacaoConfig.Repositorio(new Uri(config.Origem));
            return repo == null ? null : new Uri("https://github.com/" + repo +
                "/releases/tag/edicao-" + config.Edicao.ToString(CultureInfo.InvariantCulture));
        }
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern int GetCurrentPackageFullName(ref uint caracteres, IntPtr nome);

    static Atualizacao()
    {
        // A identidade acompanha o processo, inclusive se o pacote estiver em outro volume.
        // Só o resultado explícito APPMODEL_ERROR_NO_PACKAGE habilita o atualizador do EXE.
        const int semPacote = 15700;
        try
        {
            uint caracteres = 0;
            GerenciadaPelaStore = GetCurrentPackageFullName(ref caracteres, IntPtr.Zero) != semPacote;
        }
        catch (EntryPointNotFoundException) { GerenciadaPelaStore = false; } // Windows anterior ao AppModel.
    }

    internal static long Edicao
    {
        get { try { return AtualizacaoConfig.Ler(typeof(Atualizacao).Assembly).Edicao; } catch { return 0; } }
    }

    internal static string Versao
    {
        get { return typeof(Atualizacao).Assembly.GetName().Version.ToString(3); }
    }

    private static EventWaitHandle Criar(string id, string fase)
    {
        bool novo;
        var evento = new EventWaitHandle(false, EventResetMode.ManualReset, Nome(id, fase), out novo);
        if (!novo) { evento.Dispose(); throw new IOException("Atualização já iniciada."); }
        return evento;
    }

    private static string Nome(string id, string fase) { return "Local\\100Nome.Mix." + id + "." + fase; }
    private static string Pasta(string id) { return Path.Combine(Cache, id); }
    private static string Arquivo(string path, string nome) { return Path.Combine(path, nome); }

    internal static Task<bool> VerificarAsync(CancellationToken token)
    {
        if (GerenciadaPelaStore || !TemCanal) return Task.FromResult(false);
        return Task.Run(() => Preparar(token), token);
    }

    private static bool Preparar(CancellationToken token)
    {
        string[] args = Environment.GetCommandLineArgs();
        if (args.Length >= 2 && (args[1] == Aplicado || args[1] == Restaurado)) return false;
        var anterior = AtualizacaoConfig.Ler(typeof(Atualizacao).Assembly);
        var remoto = canalDoPerfil;
        var cfg = remoto ?? anterior;
        if (cfg.ExePublico) return false;
        Directory.CreateDirectory(Cache);
        LimparAntigos();
        byte[] manifesto;
        AtualizacaoPacote pacote;
        using (var limite = CancellationTokenSource.CreateLinkedTokenSource(token))
        using (var buffer = new MemoryStream())
        {
            limite.CancelAfter(15000);
            AtualizacaoPacote.Baixar(new Uri(cfg.Origem), buffer, 4096, limite.Token);
            manifesto = buffer.ToArray();
            pacote = AtualizacaoPacote.Validar(manifesto, cfg);
        }
        if (pacote.Edicao <= cfg.Edicao) return false;
        string id = Guid.NewGuid().ToString("N");
        string dir = Pasta(id), candidato = Arquivo(dir, "atualizar.exe");
        Directory.CreateDirectory(dir);
        bool iniciado = false;
        try
        {
            var cifra = Arquivo(dir, "pacote.bin");
            using (var limite = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (var output = new FileStream(cifra, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                limite.CancelAfter(90000);
                AtualizacaoPacote.Baixar(pacote.Endereco(cfg), output, pacote.BytesPacote, limite.Token);
            }
            token.ThrowIfCancellationRequested();
            pacote.Abrir(cifra, candidato, cfg);
            File.Delete(cifra);
            AtualizacaoConfig nova = LerConfigArquivo(candidato);
            ConferirTroca(anterior, nova, pacote.Edicao, remoto != null);
            ConferirVersoes(typeof(Atualizacao).Assembly.Location, candidato, nova.ExePublico);
            File.WriteAllBytes(Arquivo(dir, "manifesto.txt"), manifesto);
            using (var pronto = Criar(id, "pronto"))
            {
                var aplicar = Criar(id, "aplicar");
                try
                {
                    token.ThrowIfCancellationRequested();
                    using (var local = Process.GetCurrentProcess())
                    using (var lockExe = new FileStream(candidato, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        if (AtualizacaoPacote.Hash(lockExe) != pacote.HashExe) throw new CryptographicException();
                        using (var helper = Iniciar(candidato, Aplicar + " " + local.Id.ToString(
                            CultureInfo.InvariantCulture) + " " + id))
                        {
                            iniciado = true;
                            var prazo = Stopwatch.StartNew();
                            while (!pronto.WaitOne(75))
                            {
                                token.ThrowIfCancellationRequested();
                                if (helper.HasExited || prazo.ElapsedMilliseconds > 15000)
                                    throw new IOException("Não foi possível preparar a substituição do launcher.");
                            }
                        }
                    }
                    token.ThrowIfCancellationRequested();
                    lock (Guarda)
                    {
                        if (confirmacao != null) confirmacao.Dispose();
                        confirmacao = aplicar;
                        tokenPendente = token;
                        aplicar = null;
                    }
                }
                finally { if (aplicar != null) aplicar.Dispose(); }
            }
            return true;
        }
        finally { if (!iniciado) Limpar(dir); }
    }

    internal static bool AtivarPendente()
    {
        lock (Guarda)
        {
            if (confirmacao == null) return false;
            try { return !tokenPendente.IsCancellationRequested && confirmacao.Set(); }
            finally { confirmacao.Dispose(); confirmacao = null; }
        }
    }

    internal static void SinalizarInterfacePronta()
    {
        if (GerenciadaPelaStore) return;
        var args = Environment.GetCommandLineArgs();
        Guid id;
        if (args.Length != 3 || args[1] != Aplicado || !Guid.TryParseExact(args[2], "N", out id)) return;
        try { using (var evt = EventWaitHandle.OpenExisting(Nome(id.ToString("N"), "janela"))) evt.Set(); }
        catch (WaitHandleCannotBeOpenedException) { }
    }

    internal static bool ExecutarAuxiliar(out int codigo)
    {
        codigo = 0;
        var args = Environment.GetCommandLineArgs();
        if (args.Length < 2 || args[1] != Aplicar) return false;
        if (GerenciadaPelaStore) { codigo = 2; return true; }
        int pid;
        Guid id;
        if (args.Length != 4 || !int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out pid) ||
            !Guid.TryParseExact(args[3], "N", out id)) { codigo = 2; return true; }
        try { AplicarArquivo(pid, id.ToString("N")); }
        catch { codigo = 1; }
        return true;
    }

    private static AtualizacaoConfig LerConfigArquivo(string exe)
    {
        var arquivo = new FileInfo(exe);
        if (arquivo.Length > AtualizacaoPacote.Limite || arquivo.Length < 1) throw new InvalidDataException();
        // Um domínio isolado permite comparar edições que usam o mesmo AssemblyName.
        Assembly proprio = typeof(Atualizacao).Assembly;
        var dominio = AppDomain.CreateDomain("100Nome.Leitura." + Guid.NewGuid().ToString("N"),
            null, new AppDomainSetup { ApplicationBase = Path.GetDirectoryName(proprio.Location) });
        try
        {
            var leitor = (AtualizacaoLeitorIsolado)dominio.CreateInstanceFromAndUnwrap(
                proprio.Location, typeof(AtualizacaoLeitorIsolado).FullName);
            return AtualizacaoConfig.LerRecursos(leitor.LerRecursos(exe));
        }
        finally { AppDomain.Unload(dominio); }
    }

    private static void ConferirTroca(AtualizacaoConfig anterior, AtualizacaoConfig nova, long edicao, bool perfil)
    {
        if (nova.Edicao != edicao || nova.Edicao <= anterior.Edicao) throw new CryptographicException();
        if (perfil ? !nova.ExePublico || anterior.Instancia == null || anterior.Instancia != nova.Instancia :
            !anterior.MesmoCanal(nova)) throw new CryptographicException();
    }

    private static void ConferirVersoes(string anterior, string novo, bool publico)
    {
        if (!publico) return;
        var versao = AssemblyName.GetAssemblyName(novo).Version;
        var metadados = FileVersionInfo.GetVersionInfo(novo);
        if (versao.Revision != 0 || metadados.FileVersion != versao.ToString() ||
            metadados.ProductVersion != versao.ToString(3) || versao <= AssemblyName.GetAssemblyName(anterior).Version)
            throw new InvalidDataException();
    }

    private static Process Iniciar(string exe, string argumentos)
    {
        return Process.Start(new ProcessStartInfo(exe, argumentos) {
            UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) });
    }

    private static void AplicarArquivo(int pid, string id)
    {
        string dir = Pasta(id), candidato = Arquivo(dir, "atualizar.exe");
        if (!string.Equals(typeof(Atualizacao).Assembly.Location, candidato, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException();
        using (var pai = Process.GetProcessById(pid))
        using (var pronto = EventWaitHandle.OpenExisting(Nome(id, "pronto")))
        using (var aplicar = EventWaitHandle.OpenExisting(Nome(id, "aplicar")))
        {
            string destino = pai.MainModule.FileName;
            var anterior = LerConfigArquivo(destino);
            var atual = AtualizacaoConfig.Ler(typeof(Atualizacao).Assembly);
            var verificador = anterior;
#if PERFIL_REMOTO
            // O helper obtém sua raiz de confiança da central com pin TLS, independente dos arquivos da atualização.
            verificador = new AtualizacaoConfig { Modulo = PerfilRemoto.ModuloAtualizador(), Expoente = "AQAB" };
#endif
            var m = AtualizacaoPacote.Validar(File.ReadAllBytes(Arquivo(dir, "manifesto.txt")), verificador);
            ConferirTroca(anterior, atual, m.Edicao, atual.ExePublico);
            ConferirVersoes(destino, candidato, atual.ExePublico);
            if (AtualizacaoPacote.HashArquivo(candidato) != m.HashExe) throw new CryptographicException();
            string hashAntigo = AtualizacaoPacote.HashArquivo(destino);
            string chaveDestino;
            using (var bytes = new MemoryStream(Encoding.UTF8.GetBytes(Path.GetFullPath(destino).ToUpperInvariant())))
                chaveDestino = AtualizacaoPacote.Hash(bytes);
            string nomeBase = Arquivo(Path.GetDirectoryName(destino), ".100nome-" + id);
            string novo = nomeBase + ".tmp", reserva = destino + ".100nome-anterior.bak";
            bool autorizado = false, substituido = false;
            bool exclusiva;
            using (var sessao = new Mutex(false, "Local\\100Nome.Mix.arquivo." + chaveDestino, out exclusiva))
            {
                if (!exclusiva) return;
                try
                {
                    if (File.Exists(reserva))
                    {
                        var backup = LerConfigArquivo(reserva);
                        if (!backup.MesmoCanal(anterior) && (anterior.Instancia == null || backup.Instancia != anterior.Instancia))
                            throw new IOException();
                    }
                    File.Copy(candidato, novo, false);
                    pronto.Set();
                    var prazo = Stopwatch.StartNew();
                    while (!aplicar.WaitOne(75)) { if (pai.HasExited || prazo.ElapsedMilliseconds > 45000) return; }
                    autorizado = true;
                    if (!pai.WaitForExit(18000)) return;
                    if (AtualizacaoPacote.HashArquivo(destino) != hashAntigo) return;
                    // O temporário e a reserva estão no mesmo volume do EXE, inclusive se o cache estiver em C:.
                    if (AtualizacaoPacote.HashArquivo(novo) != m.HashExe) throw new CryptographicException();
                    File.Replace(novo, destino, reserva);
                    substituido = true;
                    using (var janela = Criar(id, "janela"))
                    using (var exec = IniciarVerificado(destino, m.HashExe, Aplicado + " " + id))
                    {
                        var tempo = Stopwatch.StartNew();
                        while (!janela.WaitOne(100))
                        {
                            if (exec.HasExited) throw new IOException("A nova versão encerrou antes de abrir a janela.");
                            if (tempo.ElapsedMilliseconds >= 30000)
                            {
                                // O helper só encerra o novo processo que ele mesmo iniciou.
                                // Se o processo continuar com o arquivo aberto, File.Replace
                                // falha de forma segura e a reserva continua ao lado do EXE.
                                if (!exec.HasExited) exec.Kill();
                                exec.WaitForExit(5000);
                                throw new IOException("A nova versão não confirmou a abertura da janela.");
                            }
                        }
                    }
                }
                catch
                {
                    if (autorizado && pai.HasExited)
                    {
                        if (substituido && File.Exists(reserva) && AtualizacaoPacote.HashArquivo(destino) == m.HashExe &&
                            AtualizacaoPacote.HashArquivo(reserva) == hashAntigo)
                            File.Replace(reserva, destino, null);
                        if (AtualizacaoPacote.HashArquivo(destino) == hashAntigo)
                            using (var retomado = IniciarVerificado(destino, hashAntigo, Restaurado)) { }
                    }
                    throw;
                }
                finally { Apagar(novo); }
            }
        }
    }

    private static Process IniciarVerificado(string destino, string hash, string argumentos)
    {
        using (var arquivo = new FileStream(destino, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (AtualizacaoPacote.Hash(arquivo) != hash) throw new CryptographicException();
            return Iniciar(destino, argumentos);
        }
    }

    private static void LimparAntigos()
    {
        foreach (string dir in Directory.GetDirectories(Cache))
        {
            Guid id;
            var info = new DirectoryInfo(dir);
            if (Guid.TryParseExact(info.Name, "N", out id) && (info.Attributes & FileAttributes.ReparsePoint) == 0 &&
                info.LastWriteTimeUtc < DateTime.UtcNow.AddHours(-1)) Limpar(dir);
        }
    }

    private static void Apagar(string file) { try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    private static void Limpar(string dir)
    {
        Apagar(Arquivo(dir, "pacote.bin")); Apagar(Arquivo(dir, "manifesto.txt")); Apagar(Arquivo(dir, "atualizar.exe"));
        try { Directory.Delete(dir, false); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
