internal static class MuralDaComunidade
{
#if PERFIL_REMOTO
    internal static object[] Noticias { get { return PerfilRemoto.Perfil.Noticias; } }
#else
    internal static readonly object[] Noticias = new[] {
        new { Titulo = "Como entrar", Texto = "Na aba Servidor, escolha uma entrada disponível e clique em Entrar no mix." },
        new { Titulo = "Consultar os comandos", Texto = "Pressione Ctrl+F para procurar um comando. A lista pode ser filtrada por categoria." }
    };
#endif
}
