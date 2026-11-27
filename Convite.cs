using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Windows;

[assembly: AssemblyTitle("100 Nome · Launcher do mix")]
[assembly: AssemblyDescription("Launcher do servidor de CS2 do grupo 100 Nome")]
[assembly: AssemblyProduct("100Nome CS2 Mix")]

internal static class Convite
{
#if PERFIL_REMOTO
    internal static string Dominio { get { return PerfilInstancia.DominioPrincipal; } }
#else
    internal const string Dominio = PerfilInstancia.DominioPrincipal;
#endif
    internal const int Porta = 27015;

    [STAThread]
    private static int Main()
    {
#if PERFIL_EXEMPLO
        MessageBox.Show("Compilação de exemplo. Os serviços de jogo, cadastro e atualização estão desativados.",
            "Launcher de exemplo", MessageBoxButton.OK, MessageBoxImage.Information);
        return 0;
#else
#if PERFIL_REMOTO
        while (true)
        {
            try { PerfilRemoto.PrepararInicio(); break; }
            catch (Exception)
            {
                if (Environment.GetCommandLineArgs().Length > 1) return 1;
                if (MessageBox.Show("A central de cadastro não respondeu com um perfil válido. Confira a conexão e tente novamente.",
                    "Acesso ao 100Nome Mix", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return 1;
            }
        }
#endif
        int codigo;
        if (Atualizacao.ExecutarAuxiliar(out codigo)) return codigo;
        var aplicativo = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        try
        {
            bool consentiu = false;
#if PERFIL_REMOTO
            var conhecido = LicencaPC.CodigoDaLicencaSalva();
            if (conhecido == null)
            {
                consentiu = PerfilRemoto.PedirConsentimento(null, true);
                if (!consentiu) return 0;
            }
#endif
            var codigoPC = LicencaPC.Codigo();
#if PERFIL_REMOTO
            if (conhecido != null && conhecido != codigoPC)
            {
                consentiu = PerfilRemoto.PedirConsentimento(null, true);
                if (!consentiu) return 0;
            }
#endif
            if (new JanelaAtivacao(codigoPC, consentiu).ShowDialog() != true) return 0;
        }
        catch (Exception)
        {
            MessageBox.Show("Não foi possível identificar este PC. Abra o launcher no seu perfil normal do Windows.",
                "Acesso ao 100Nome Mix", MessageBoxButton.OK, MessageBoxImage.Warning);
            return 1;
        }
        var launcher = new Launcher();
        aplicativo.MainWindow = launcher.Janela;
        aplicativo.ShutdownMode = ShutdownMode.OnMainWindowClose;
        return aplicativo.Run(launcher.Janela);
#endif
    }

    internal static bool SenhaValida(string senha)
    {
        if (string.IsNullOrEmpty(senha) || senha[0] == '\uFEFF') return false;
        try { if (new UTF8Encoding(false, true).GetByteCount(senha) > 512) return false; }
        catch (EncoderFallbackException) { return false; }
        foreach (char caractere in senha)
            if (char.IsControl(caractere)) return false;
        return true;
    }

    internal static ProcessStartInfo PrepararConexao(IPEndPoint destino, string senha)
    {
        if (destino == null) throw new ArgumentNullException("destino");
        if (!SenhaValida(senha)) throw new InvalidDataException("Senha do convite inválida.");
        string url = "steam://connect/" + destino + "/" + Uri.EscapeDataString(senha);
        return new ProcessStartInfo(url) { UseShellExecute = true };
    }

}
