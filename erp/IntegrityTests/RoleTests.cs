using GraficaCaixa;
using Microsoft.Data.Sqlite;

// Perfis de acesso: criar copiando outro, editar, usar num usuário, excluir só sem usuários; Administrador fixo.
public static class RoleTests
{
    static int passed;
    static void Check(bool ok,string name){if(!ok)throw new Exception("FALHOU: "+name);Console.WriteLine("OK "+name);passed++;}
    static bool Throws<T>(Action action) where T:Exception{try{action();return false;}catch(T){return true;}}
    public static void Run()
    {
        var folder=Path.Combine(Path.GetTempPath(),"roles-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new DataStore(folder);store.EnsureInitialized();const string password="Teste-Perfil!2026";
            store.Update(d=>{d.Prompt12.LoginPassword=LoginSecurity.Create(password);return 0;});
            var security=new IdentitySecurity(store);security.Migrate();
            var admin=security.Login("grafica",password,"teste")??throw new Exception("Login esperado.");
            var before=security.RoleList(admin.User);
            Check(before.Select(x=>x.Name).SequenceEqual(new[]{"Administrador","Financeiro"})&&before.All(x=>x.BuiltIn),"começa com os dois perfis fixos");
            security.CreateRole(admin.User,new("Balcão","Financeiro"),"teste");
            var roles=security.RoleList(admin.User);var counter=roles.Single(x=>x.Name=="Balcão");var finance=roles.Single(x=>x.Name=="Financeiro");
            Check(!counter.BuiltIn&&counter.Allowed==finance.Allowed,"Balcão criado com as mesmas permissões do Financeiro");
            Check(Throws<InvalidOperationException>(()=>security.CreateRole(admin.User,new("balcão","Financeiro"),"teste")),"não cria dois perfis com o mesmo nome");
            Check(Throws<InvalidOperationException>(()=>security.CreateRole(admin.User,new("ab","Financeiro"),"teste")),"nome curto é recusado");
            security.SavePermissions(admin.User,new PermissionEdit(null,"Balcão",new(){["view.reports"]=false,["cash.withdraw"]=false}),"teste");
            var panel=security.PermissionPanel(admin.User,null,"Balcão");
            Check(!panel.Single(x=>x.Key=="view.reports").Allowed&&panel.Single(x=>x.Key=="view.cash").Allowed,"editar o perfil Balcão: tira Relatórios e mantém Caixa");
            Check(Throws<InvalidOperationException>(()=>security.SavePermissions(admin.User,new PermissionEdit(null,"Administrador",new(){["view.reports"]=false}),"teste")),"perfil Administrador é fixo (não pode ser limitado)");
            security.SaveUser(admin.User,new UserEdit(null,"Atendente","atendente","Balcão",true,"Senha-Forte!2026"),"teste");
            Check(security.RoleList(admin.User).Single(x=>x.Name=="Balcão").Users==1,"usuário novo pode usar o perfil Balcão");
            var atendente=security.Login("atendente","Senha-Forte!2026","teste")??throw new Exception("Login do atendente esperado.");
            var effective=security.Effective(atendente.User);
            Check(!effective["view.reports"]&&effective["view.cash"]&&!effective["system.users"],"permissões efetivas do atendente seguem o perfil Balcão");
            Check(Throws<InvalidOperationException>(()=>security.DeleteRole(admin.User,"Balcão","teste")),"perfil com usuário não pode ser excluído");
            Check(Throws<InvalidOperationException>(()=>security.DeleteRole(admin.User,"Financeiro","teste")),"perfis fixos não podem ser excluídos");
            security.CreateRole(admin.User,new("Produção","Balcão"),"teste");security.DeleteRole(admin.User,"Produção","teste");
            Check(security.RoleList(admin.User).All(x=>x.Name!="Produção"),"perfil sem usuários é excluído");
            Check(Throws<UnauthorizedAccessException>(()=>security.CreateRole(atendente.User,new("Hacker","Administrador"),"teste")),"quem não tem Usuários e Permissões não cria perfil");
            using var c=new SqliteConnection($"Data Source={store.DatabasePath};Pooling=False");c.Open();using var cmd=c.CreateCommand();
            cmd.CommandText="SELECT COUNT(*) FROM audit_trail WHERE action IN ('Perfil criado','Perfil excluído')";
            Check(Convert.ToInt32(cmd.ExecuteScalar())==3,"criação e exclusão de perfis ficam na auditoria");
            Console.WriteLine($"PERFIS: {passed} testes aprovados");
        }
        finally{SqliteConnection.ClearAllPools();try{Directory.Delete(folder,true);}catch(IOException){}}
    }
}
