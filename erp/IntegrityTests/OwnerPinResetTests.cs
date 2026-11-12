using GraficaCaixa;
using Microsoft.Data.Sqlite;

// "Esqueci o PIN": só Administrador, confirmando a senha de login; PIN antigo deixa de valer; tudo na auditoria.
public static class OwnerPinResetTests
{
    static int passed;
    static void Check(bool ok,string name){if(!ok)throw new Exception("FALHOU: "+name);Console.WriteLine("OK "+name);passed++;}
    static bool Throws<T>(Action action) where T:Exception{try{action();return false;}catch(T){return true;}}
    public static void Run()
    {
        var folder=Path.Combine(Path.GetTempPath(),"pin-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new DataStore(folder);store.EnsureInitialized();const string password="Teste-Pin!2026";
            store.Update(d=>{d.Prompt12.LoginPassword=LoginSecurity.Create(password);d.Prompt12.OwnerPin=OwnerSecurity.Create("111111");return 0;});
            var security=new IdentitySecurity(store);security.Migrate();
            var admin=security.Login("grafica",password,"teste")??throw new Exception("Login esperado.");
            Check(Throws<UnauthorizedAccessException>(()=>security.ResetOwnerPin(admin.User,new("senha-errada","222222"),"teste")),"senha de login errada não troca o PIN");
            Check(Throws<InvalidOperationException>(()=>security.ResetOwnerPin(admin.User,new(password,"12ab"),"teste")),"PIN novo curto ou com letras é recusado");
            security.OwnerAuthenticate(admin.User,new("111111"),"teste");Check(true,"PIN antigo ainda vale antes da redefinição");
            security.ResetOwnerPin(admin.User,new(password,"222222"),"teste");
            Check(Throws<UnauthorizedAccessException>(()=>security.OwnerAuthenticate(admin.User,new("111111"),"teste")),"depois de redefinir, o PIN antigo não vale");
            security.OwnerAuthenticate(admin.User,new("222222"),"teste");Check(true,"o PIN novo desbloqueia a Área do proprietário");
            var financeiro=admin.User with{Role="Financeiro"};
            Check(Throws<UnauthorizedAccessException>(()=>security.ResetOwnerPin(financeiro,new(password,"333333"),"teste")),"perfil que não é Administrador não redefine");
            using var c=new SqliteConnection($"Data Source={store.DatabasePath};Pooling=False");c.Open();using var cmd=c.CreateCommand();
            cmd.CommandText="SELECT COUNT(*) FROM audit_trail WHERE action='PIN do proprietário redefinido'";var done=Convert.ToInt32(cmd.ExecuteScalar());
            cmd.CommandText="SELECT COUNT(*) FROM audit_trail WHERE action='Redefinição do PIN recusada'";var refused=Convert.ToInt32(cmd.ExecuteScalar());
            Check(done==1&&refused==1,"redefinição e tentativa recusada ficam na auditoria");
            Console.WriteLine($"PIN: {passed} testes aprovados");
        }
        finally{SqliteConnection.ClearAllPools();try{Directory.Delete(folder,true);}catch(IOException){}}
    }
}
