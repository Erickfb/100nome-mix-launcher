using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

internal sealed class JanelaConvite : Window
{
    internal const string AvisoVinculo = "Ao vincular, o launcher envia uma vez o identificador da instalação do Windows " +
        "(MachineGuid) à central do 100Nome Mix por uma conexão criptografada. A central confere o cadastro, " +
        "descarta o identificador e guarda a impressão da chave pública do PC. Depois você pode entrar pelas três entradas sem digitar a senha.";

    private readonly PasswordBox campo = new PasswordBox { MaxLength = 512, Padding = new Thickness(9) };
    internal bool Vincular { get; private set; }
    internal string SenhaDigitada { get; private set; }

    internal JanelaConvite(Window principal, bool entradaDireta)
    {
        Owner = principal;
        Title = "Entrar no mix";
        Width = Math.Min(490, SystemParameters.WorkArea.Width);
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = principal.Background;
        Foreground = principal.Foreground;
        FontFamily = principal.FontFamily;
        var painel = new StackPanel { Margin = new Thickness(22) };
        Content = painel;
        painel.Children.Add(new TextBlock { Text = "Vincular este PC", FontSize = 21, FontWeight = FontWeights.SemiBold });
        painel.Children.Add(new TextBlock { Text = AvisoVinculo, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,8,0,12) });
        var vincular = Botao(principal, "Vincular e entrar", "#7943CD");
        vincular.Click += delegate { Vincular = true; DialogResult = true; };
        painel.Children.Add(vincular);
        if (entradaDireta)
        {
            var rotulo = new Label { Content = "Prefere digitar a senha do mix?", Target = campo, Foreground = principal.Foreground,
                Padding = new Thickness(0), Margin = new Thickness(0,20,0,5) };
            painel.Children.Add(rotulo);
            AutomationProperties.SetName(campo, "Senha do mix");
            painel.Children.Add(campo);
            var usarSenha = Botao(principal, "Entrar com senha", "#35313D");
            usarSenha.Margin = new Thickness(0,8,0,0);
            usarSenha.IsEnabled = false;
            campo.PasswordChanged += delegate {
                using (var valor = campo.SecurePassword) usarSenha.IsEnabled = valor.Length > 0;
            };
            usarSenha.IsDefault = true;
            usarSenha.Click += delegate {
                var senha = campo.Password;
                if (!Convite.SenhaValida(senha))
                {
                    rotulo.Content = "Confira a senha do mix.";
                    campo.Focus();
                    return;
                }
                SenhaDigitada = senha;
                DialogResult = true;
            };
            painel.Children.Add(usarSenha);
        }
        var cancelar = Botao(principal, "Cancelar", "#26232C");
        cancelar.Margin = new Thickness(0,16,0,0);
        cancelar.IsCancel = true;
        painel.Children.Add(cancelar);
        Loaded += delegate { if (entradaDireta) campo.Focus(); else cancelar.Focus(); };
        Closed += delegate { campo.Clear(); };
    }

    private static Button Botao(Window principal, string texto, string fundo)
    {
        return new Button { Content = texto, HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(14,7,14,7), Style = (Style)principal.FindResource("button"),
            Background = (Brush)new BrushConverter().ConvertFromString(fundo) };
    }
}
