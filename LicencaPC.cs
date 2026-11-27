using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

internal static class LicencaPC
{
    private const string Cabecalho = "100NOME-PC-1";
    private static readonly string Pasta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        PerfilInstancia.PastaDados, "acesso");

    internal static string GuidDaInstalacao()
    {
        // A identidade pública combina a instalação do Windows e uma chave própria do perfil.
        string maquina;
        using (var baseLocal = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,
            Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32))
        using (var reg = baseLocal.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
            maquina = reg == null ? null : reg.GetValue("MachineGuid") as string;
        Guid guid;
        if (!Guid.TryParse(maquina, out guid)) throw new InvalidDataException("Identidade do Windows indisponível.");

        return guid.ToString("N");
    }

    internal static RSACryptoServiceProvider AbrirChave()
    {
        var usuario = new CspParameters(24) { KeyContainerName = PerfilInstancia.ChaveLocalPC,
            KeyNumber = (int)KeyNumber.Signature, Flags = CspProviderFlags.UseNonExportableKey | CspProviderFlags.NoPrompt };
        var rsa = new RSACryptoServiceProvider(2048, usuario);
        if (rsa.PublicOnly)
        {
            rsa.Dispose();
            throw new CryptographicException("Chave do perfil indisponível.");
        }
        rsa.PersistKeyInCsp = true;
        return rsa;
    }

    internal static string Codigo()
    {
        using (var rsa = AbrirChave())
            return CalcularCodigo(GuidDaInstalacao(), rsa);
    }

    internal static string CalcularCodigo(string guid, RSACryptoServiceProvider rsa)
    {
        using (var hash = SHA256.Create())
        {
            byte[] dados = Encoding.ASCII.GetBytes("100NOME-DISPOSITIVO-1\n" + guid + "\n" +
                Convert.ToBase64String(rsa.ExportCspBlob(false)));
            string id = BitConverter.ToString(hash.ComputeHash(dados), 0, 16).Replace("-", "");
            return "PC1-" + id.Substring(0,8) + "-" + id.Substring(8,8) + "-" + id.Substring(16,8) + "-" + id.Substring(24,8);
        }
    }

    private static byte[] LerArquivo(string caminho)
    {
        using (var arquivo = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
        using (var leitor = new BinaryReader(arquivo))
        {
            if (arquivo.Length == 0 || arquivo.Length > 2048) throw new InvalidDataException("Formato de autorização inválido.");
            return leitor.ReadBytes((int)arquivo.Length);
        }
    }

    internal static void Validar(byte[] dados, string codigo)
    {
        if (dados.Length > 2048) throw new InvalidDataException("Autorização inválida.");
        string[] campos = new UTF8Encoding(false, true).GetString(dados).Split('\n');
        if (campos.Length != 4 || campos[0] != Cabecalho || campos[3] != "" || campos[2].Length != 512)
            throw new InvalidDataException("Escolha o arquivo .100nome emitido pelo administrador.");
        if (!string.Equals(campos[1], codigo, StringComparison.Ordinal))
            throw new InvalidDataException("Esta autorização foi emitida para outro código de PC.");
        using (var rsa = new RSACryptoServiceProvider(new CspParameters(24) { Flags = CspProviderFlags.CreateEphemeralKey }))
        {
            rsa.PersistKeyInCsp = false;
            rsa.ImportParameters(new RSAParameters { Modulus = Convert.FromBase64String(PerfilInstancia.ModuloEmissor), Exponent = new byte[] {1,0,1} });
            byte[] conteudo = Encoding.ASCII.GetBytes(Cabecalho + "\n" + codigo + "\n");
            if (!rsa.VerifyData(conteudo, "SHA256", Convert.FromBase64String(campos[2])))
                throw new InvalidDataException("A assinatura da autorização é inválida.");
        }
    }

    private static string Arquivo { get { return Path.Combine(Pasta, "licenca.100nome"); } }

    internal static bool Autorizado(string codigo)
    {
        try { Validar(LerArquivo(Arquivo), codigo); return true; }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (InvalidDataException) { }
        catch (CryptographicException) { }
        catch (FormatException) { }
        catch (DecoderFallbackException) { }
        return false;
    }

    internal static string CredencialDaEntrada()
    {
        return LerCredencial(Codigo());
    }

    internal static string LerCredencial(string codigo)
    {
        var licenca = LerArquivo(Arquivo);
        Validar(licenca, codigo);
        return Convert.ToBase64String(licenca);
    }

    internal static void Importar(string origem, string codigo)
    {
        SalvarAssinada(LerArquivo(origem), codigo);
    }

    internal static void SalvarAssinada(byte[] dados, string codigo)
    {
        Validar(dados, codigo);
        Directory.CreateDirectory(Pasta);
        string temp = Path.Combine(Pasta, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var f = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) f.Write(dados, 0, dados.Length);
            if (File.Exists(Arquivo)) File.Replace(temp, Arquivo, null); else File.Move(temp, Arquivo);
        }
        finally { File.Delete(temp); }
    }
}
