using System;
using System.Windows;
using System.Windows.Controls;

// Ponto de entrada exclusivo da demonstração. A comunidade configurada usa Convite.Main.
internal sealed partial class Launcher
{
    private Launcher(string titulo)
    {
        MontarJanela();
        Janela.Title = titulo;
        foreach (string nome in new[] { "Hero", "ResumoServidor", "AbaNoticias", "AbaEstatisticas", "AbaServidor", "Entrar" })
            Controle<FrameworkElement>(nome).Visibility = Visibility.Collapsed;
        Controle<RadioButton>("AbaComandos").IsChecked = true;
        Controle<Grid>("Guia").Visibility = Visibility.Visible;
        legendaEntrada = titulo;
        Controle<TextBlock>("Mensagem").Text = legendaEntrada;
        ConfigurarGuia();
        aviso.Interval = TimeSpan.FromSeconds(6);
        aviso.Tick += delegate { aviso.Stop(); Controle<TextBlock>("Mensagem").Text = legendaEntrada; };
        Janela.Closed += delegate { aviso.Stop(); fecharAtualizador.Dispose(); fecharCentral.Dispose(); };
    }

    [STAThread]
    internal static int Main()
    {
        var aplicativo = new Application();
        return aplicativo.Run(new Launcher("Guia de comandos · demonstração offline").Janela);
    }
}
