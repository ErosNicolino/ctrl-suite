using Microsoft.Data.Sqlite;
using System.Text.Json;
namespace GraficaCaixa;

public sealed record PermissionDefinition(string Key,string Group,string Label,bool Financeiro=false);
public sealed record PermissionState(string Key,string Group,string Label,bool Profile,bool? Exception,bool Allowed,string Origin);
public sealed record PermissionEdit(Guid? UserId,string? Role,Dictionary<string,bool?> Values);
public sealed record SessionRevoke(Guid UserId);
public sealed record RoleSummary(string Name,bool BuiltIn,int Users,int Allowed,int Total);
public sealed record RoleCreate(string Name,string CopyFrom);
public sealed record RoleDelete(string Name);
public static class Permissions
{
    public static readonly PermissionDefinition[] Catalog = [
        new("view.home","Visualização","Início",true),new("view.orders","Visualização","Pedidos",true),
        new("view.budget","Visualização","Orçamentos",true),new("view.purchases","Visualização","Compras",true),
        new("view.products","Visualização","Produtos",true),new("view.customers","Visualização","Clientes",true),
        new("view.debtors","Visualização","Devedores",true),new("view.prospects","Visualização","Orçamentos em acompanhamento",true),
        new("view.cash","Visualização","Caixa",true),new("view.closed","Visualização","Fechamentos",true),
        new("view.reports","Visualização","Relatórios",true),new("view.reminders","Visualização","Lembretes",true),
        new("orders.create","Pedidos","Criar",true),new("orders.edit","Pedidos","Editar informações",true),
        new("orders.items","Pedidos","Alterar itens",true),new("orders.quantity","Pedidos","Alterar quantidade",true),
        new("orders.value","Pedidos","Alterar valores",true),new("orders.stage","Pedidos","Alterar etapa",true),
        new("orders.deliver","Pedidos","Entregar",true),new("orders.cancel","Pedidos","Cancelar / arquivar"),
        new("finance.receive","Financeiro","Receber pagamento / lançar venda",true),new("finance.correct","Financeiro","Corrigir pagamento / valores"),
        new("finance.method","Financeiro","Alterar forma de pagamento"),new("finance.reverse","Financeiro","Estornar / cancelar lançamento"),
        new("cash.open","Financeiro","Abrir caixa",true),new("cash.withdraw","Financeiro","Sangria"),new("cash.adjust","Financeiro","Ajuste de caixa"),new("cash.close","Financeiro","Fechar caixa",true),
        new("cash.rectify","Financeiro","Retificar fechamento"),new("finance.receipt","Financeiro","Recibo avulso",true),
        new("customers.edit","Cadastros","Cadastrar / editar clientes",true),new("products.edit","Cadastros","Cadastrar / editar produtos",true),
        new("costs.edit","Cadastros","Alterar custos"),new("purchases.edit","Cadastros","Administrar compras",true),
        new("budget.edit","Cadastros","Alterar parâmetros de orçamento"),new("debtors.edit","Cadastros","Administrar devedores",true),
        new("prospects.edit","Cadastros","Administrar orçamentos em acompanhamento",true),new("reminders.edit","Cadastros","Administrar lembretes",true),
        new("system.users","Sistema","Usuários e Permissões"),new("system.audit","Sistema","Auditoria"),
        new("system.settings","Sistema","Configurações"),new("system.backups","Sistema","Backups / restauração"),
        new("system.owner","Sistema","Área do proprietário")];
    public static bool Known(string key)=>Catalog.Any(p=>p.Key==key);
    public static Dictionary<string,bool> Resolve(SqliteConnection c,UserIdentity actor)
    {
        using(var check=c.CreateCommand())
        {
            check.CommandText="SELECT u.role FROM auth_users u JOIN auth_sessions s ON s.user_id=u.id LEFT JOIN auth_devices d ON d.id=s.device_id WHERE u.id=@u AND s.id=@s AND u.active=1 AND s.revoked_at IS NULL AND s.access_expires>@now AND COALESCE(s.absolute_expires,s.refresh_expires)>@now AND (s.device_id IS NULL OR (d.id IS NOT NULL AND d.revoked_at IS NULL))";
            check.Parameters.AddWithValue("@u",actor.UserId.ToString());check.Parameters.AddWithValue("@s",actor.SessionId.ToString());check.Parameters.AddWithValue("@now",DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            var role=check.ExecuteScalar()?.ToString()??throw new SessionInvalidException();
            var result=Catalog.ToDictionary(x=>x.Key,x=>role=="Administrador"||role=="Financeiro"&&x.Financeiro);
            using var rules=c.CreateCommand();rules.CommandText="SELECT permission,allowed,0 AS priority FROM auth_role_permissions WHERE role=@r UNION ALL SELECT permission,allowed,1 AS priority FROM auth_user_permissions WHERE user_id=@u ORDER BY priority";rules.Parameters.AddWithValue("@r",role);rules.Parameters.AddWithValue("@u",actor.UserId.ToString());
            using var reader=rules.ExecuteReader();while(reader.Read())if(result.ContainsKey(reader.GetString(0)))result[reader.GetString(0)]=reader.GetInt32(1)==1;return result;
        }
    }
}
public sealed class SessionInvalidException : Exception;
public sealed partial class IdentitySecurity
{
    static void EnsureManager(SqliteConnection c)
    {
        using var cmd=Command(c,"SELECT COUNT(*) FROM auth_users u WHERE u.active=1 AND u.role='Administrador' AND COALESCE((SELECT allowed FROM auth_user_permissions p WHERE p.user_id=u.id AND p.permission='system.users'),(SELECT allowed FROM auth_role_permissions p WHERE p.role=u.role AND p.permission='system.users'),1)=1");
        if(Convert.ToInt32(cmd.ExecuteScalar())==0)throw new InvalidOperationException("Preserve um Administrador ativo com acesso a Usuários e Permissões.");
    }
    public void MigratePermissions()=>store.SecurityWork(c=>
    {
        using var tx=c.BeginTransaction();
        Run(c,"CREATE TABLE IF NOT EXISTS auth_roles(name TEXT PRIMARY KEY,created_at INTEGER NOT NULL); CREATE TABLE IF NOT EXISTS auth_role_permissions(role TEXT NOT NULL,permission TEXT NOT NULL,allowed INTEGER NOT NULL CHECK(allowed IN (0,1)),PRIMARY KEY(role,permission)); CREATE TABLE IF NOT EXISTS auth_user_permissions(user_id TEXT NOT NULL REFERENCES auth_users(id),permission TEXT NOT NULL,allowed INTEGER NOT NULL CHECK(allowed IN (0,1)),PRIMARY KEY(user_id,permission));");
        foreach(var table in new[]{"audit_trail","remote_operations"})
        {
            using var info=Command(c,$"PRAGMA table_info({table})");var columns=new HashSet<string>();using(var r=info.ExecuteReader())while(r.Read())columns.Add(r.GetString(1));
            foreach(var col in table=="audit_trail"?new[]{"user_id","user_login","session_id"}:new[]{"actor_id","payload_hash"})
                if(!columns.Contains(col))Run(c,$"ALTER TABLE {table} ADD COLUMN {col} TEXT NOT NULL DEFAULT ''");
        }
        tx.Commit();
        AllowCustomRoles(c);
        return 0;
    });
    // Bancos antigos travam o perfil do usuário em 'Administrador'/'Financeiro' (CHECK na tabela). Para aceitar perfis criados em
    // Perfis de acesso, a tabela é refeita sem essa trava (procedimento padrão do SQLite: copia tudo, troca a tabela, recria índices).
    // O perfil continua validado pelo servidor (SaveUser só aceita perfis existentes).
    static void AllowCustomRoles(SqliteConnection c)
    {
        string? sql;using(var q=Command(c,"SELECT sql FROM sqlite_master WHERE type='table' AND name='auth_users'"))sql=q.ExecuteScalar() as string;
        if(sql==null||!sql.Contains("CHECK(role IN",StringComparison.OrdinalIgnoreCase))return;
        var extras=new List<string>();using(var q=Command(c,"SELECT sql FROM sqlite_master WHERE tbl_name='auth_users' AND type IN ('index','trigger') AND sql IS NOT NULL"))using(var r=q.ExecuteReader())while(r.Read())extras.Add(r.GetString(0));
        var created=System.Text.RegularExpressions.Regex.Replace(sql,@"\s*CHECK\s*\(\s*role\s+IN\s*\([^)]*\)\s*\)","",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        created=System.Text.RegularExpressions.Regex.Replace(created,@"^CREATE TABLE\s+(IF NOT EXISTS\s+)?""?auth_users""?","CREATE TABLE auth_users_rebuild",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        Run(c,"PRAGMA foreign_keys=OFF");
        try
        {
            using var tx=c.BeginTransaction();
            Run(c,"DROP TABLE IF EXISTS auth_users_rebuild");Run(c,created);
            Run(c,"INSERT INTO auth_users_rebuild SELECT * FROM auth_users");
            using(var a=Command(c,"SELECT COUNT(*) FROM auth_users"))using(var b=Command(c,"SELECT COUNT(*) FROM auth_users_rebuild"))if(Convert.ToInt64(a.ExecuteScalar())!=Convert.ToInt64(b.ExecuteScalar()))throw new InvalidOperationException("Migração de perfis: contagem de usuários diferente; nada foi alterado.");
            Run(c,"DROP TABLE auth_users");Run(c,"ALTER TABLE auth_users_rebuild RENAME TO auth_users");
            foreach(var extra in extras)Run(c,extra);
            tx.Commit();
        }
        finally{Run(c,"PRAGMA foreign_keys=ON");}
    }
    static void Require(SqliteConnection c,UserIdentity user,string permission)
    {if(!Permissions.Resolve(c,user).GetValueOrDefault(permission))throw new UnauthorizedAccessException("Acesso recusado.");}
    public void Require(UserIdentity user,string permission)=>store.SecurityWork(c=>{Require(c,user,permission);return 0;});
    public Dictionary<string,bool> Effective(UserIdentity user)=>store.SecurityWork(c=>Permissions.Resolve(c,user));
    public IReadOnlyList<PermissionState> PermissionPanel(UserIdentity actor,Guid? userId,string? role)=>store.SecurityWork(c=>
    {
        Require(c,actor,"system.users");
        if(userId.HasValue){using var user=Command(c,"SELECT role FROM auth_users WHERE id=@u",("@u",userId.ToString()));role=user.ExecuteScalar()?.ToString()??throw new InvalidOperationException("Usuário não encontrado.");}
        if(!RoleNames(c).Contains(role))throw new InvalidOperationException("Perfil inválido.");
        var profile=Permissions.Catalog.ToDictionary(x=>x.Key,x=>role=="Administrador"||x.Financeiro);var exceptions=new Dictionary<string,bool>();
        using(var cmd=Command(c,"SELECT permission,allowed FROM auth_role_permissions WHERE role=@r",("@r",role)))using(var r=cmd.ExecuteReader())while(r.Read())profile[r.GetString(0)]=r.GetInt32(1)==1;
        if(userId.HasValue)using(var cmd=Command(c,"SELECT permission,allowed FROM auth_user_permissions WHERE user_id=@u",("@u",userId.ToString())))using(var r=cmd.ExecuteReader())while(r.Read())exceptions[r.GetString(0)]=r.GetInt32(1)==1;
        return (IReadOnlyList<PermissionState>)Permissions.Catalog.Select(p=>{bool? exception=exceptions.TryGetValue(p.Key,out var val)?val:null;var allowed=exception??profile[p.Key];return new PermissionState(p.Key,p.Group,p.Label,profile[p.Key],exception,allowed,exception.HasValue?(allowed?"✓ Exceção individual":"✕ Bloqueado para este usuário"):(allowed?"✓ Permitido pelo perfil":"✕ Negado pelo perfil"));}).ToList();
    });
    public void SavePermissions(UserIdentity actor,PermissionEdit edit,string origin)=>store.SecurityWork(c=>
    {
        Require(c,actor,"system.users");if(edit.Values==null||edit.Values.Keys.Any(k=>!Permissions.Known(k)))throw new InvalidOperationException("Permissão inválida.");
        if(edit.UserId.HasValue){using var exists=Command(c,"SELECT COUNT(*) FROM auth_users WHERE id=@u",("@u",edit.UserId.ToString()));if(Convert.ToInt32(exists.ExecuteScalar())!=1)throw new InvalidOperationException("Usuário não encontrado.");}
        else if(!RoleNames(c).Contains(edit.Role))throw new InvalidOperationException("Perfil inválido.");
        else if(edit.Role=="Administrador")throw new InvalidOperationException("O perfil Administrador é fixo e sempre tem acesso a tudo. Para limitar alguém, use outro perfil ou uma exceção no próprio usuário.");
        using var tx=c.BeginTransaction();var table=edit.UserId.HasValue?"auth_user_permissions":"auth_role_permissions";var field=edit.UserId.HasValue?"user_id":"role";var id=edit.UserId?.ToString()??edit.Role!;
        var before=new Dictionary<string,bool>();using(var read=Command(c,$"SELECT permission,allowed FROM {table} WHERE {field}=@id",("@id",id)))using(var r=read.ExecuteReader())while(r.Read())before[r.GetString(0)]=r.GetInt32(1)==1;
        foreach(var (key,value) in edit.Values){Run(c,$"DELETE FROM {table} WHERE {field}=@id AND permission=@p",("@id",id),("@p",key));if(value.HasValue)Run(c,$"INSERT INTO {table} VALUES(@id,@p,@v)",("@id",id),("@p",key),("@v",value.Value?1:0));}
        // Always retain one active administrator who can manage users and recover settings.
        EnsureManager(c);
        Run(c,"UPDATE auth_sessions SET owner_until=0 WHERE user_id IN (SELECT id FROM auth_users WHERE id=@id OR role=@id)",("@id",id));
        Audit(c,"Permissões alteradas",origin,actor,id);
        Run(c,"UPDATE audit_trail SET old_value=@old,new_value=@new,user_id=@u,user_login=@l,session_id=@s WHERE rowid=last_insert_rowid()",("@old",JsonSerializer.Serialize(before)),("@new",JsonSerializer.Serialize(edit.Values)),("@u",actor.UserId.ToString()),("@l",actor.Login),("@s",actor.SessionId.ToString()));
        tx.Commit();return 0;
    });
    // Perfis: Administrador e Financeiro são fixos; os demais são criados aqui copiando as permissões de outro perfil.
    static string[] RoleNames(SqliteConnection c){var names=new List<string>(Roles);using var cmd=Command(c,"SELECT name FROM auth_roles ORDER BY name");using var r=cmd.ExecuteReader();while(r.Read())names.Add(r.GetString(0));return names.ToArray();}
    public IReadOnlyList<string> RoleNames(UserIdentity actor)=>store.SecurityWork(c=>{Require(c,actor,"system.users");return (IReadOnlyList<string>)RoleNames(c);});
    public IReadOnlyList<RoleSummary> RoleList(UserIdentity actor)=>store.SecurityWork(c=>
    {
        Require(c,actor,"system.users");var list=new List<RoleSummary>();
        foreach(var name in RoleNames(c))
        {
            var profile=Permissions.Catalog.ToDictionary(x=>x.Key,x=>name=="Administrador"||name=="Financeiro"&&x.Financeiro);
            using(var cmd=Command(c,"SELECT permission,allowed FROM auth_role_permissions WHERE role=@r",("@r",name)))using(var r=cmd.ExecuteReader())while(r.Read())if(profile.ContainsKey(r.GetString(0)))profile[r.GetString(0)]=r.GetInt32(1)==1;
            using var users=Command(c,"SELECT COUNT(*) FROM auth_users WHERE role=@r AND active=1",("@r",name));
            list.Add(new RoleSummary(name,Roles.Contains(name),Convert.ToInt32(users.ExecuteScalar()),name=="Administrador"?Permissions.Catalog.Length:profile.Count(x=>x.Value),Permissions.Catalog.Length));
        }
        return (IReadOnlyList<RoleSummary>)list;
    });
    public void CreateRole(UserIdentity actor,RoleCreate edit,string origin)=>store.SecurityWork(c=>
    {
        Require(c,actor,"system.users");var name=(edit.Name??"").Trim();
        if(name.Length<3||name.Length>40||!name.Any(char.IsLetter))throw new InvalidOperationException("Informe o nome do perfil (3 a 40 caracteres).");
        var names=RoleNames(c);if(names.Any(x=>x.Equals(name,StringComparison.CurrentCultureIgnoreCase)))throw new InvalidOperationException("Já existe um perfil com esse nome.");
        if(!names.Contains(edit.CopyFrom))throw new InvalidOperationException("Escolha o perfil a copiar.");
        var copy=Permissions.Catalog.ToDictionary(x=>x.Key,x=>edit.CopyFrom=="Administrador"||edit.CopyFrom=="Financeiro"&&x.Financeiro);
        using(var cmd=Command(c,"SELECT permission,allowed FROM auth_role_permissions WHERE role=@r",("@r",edit.CopyFrom)))using(var r=cmd.ExecuteReader())while(r.Read())if(copy.ContainsKey(r.GetString(0)))copy[r.GetString(0)]=r.GetInt32(1)==1;
        using var tx=c.BeginTransaction();
        Run(c,"INSERT INTO auth_roles(name,created_at) VALUES(@n,@t)",("@n",name),("@t",clock().ToUnixTimeSeconds()));
        foreach(var (key,value) in copy)Run(c,"INSERT INTO auth_role_permissions VALUES(@r,@p,@v)",("@r",name),("@p",key),("@v",value?1:0));
        Audit(c,"Perfil criado",origin,actor,name);tx.Commit();return 0;
    });
    public void DeleteRole(UserIdentity actor,string name,string origin)=>store.SecurityWork(c=>
    {
        Require(c,actor,"system.users");if(Roles.Contains(name))throw new InvalidOperationException("Administrador e Financeiro são perfis fixos.");
        using(var users=Command(c,"SELECT COUNT(*) FROM auth_users WHERE role=@r",("@r",name)))if(Convert.ToInt32(users.ExecuteScalar())>0)throw new InvalidOperationException("Este perfil ainda tem usuários. Troque o perfil deles antes de excluir.");
        using var tx=c.BeginTransaction();Run(c,"DELETE FROM auth_role_permissions WHERE role=@r",("@r",name));Run(c,"DELETE FROM auth_roles WHERE name=@r",("@r",name));
        Audit(c,"Perfil excluído",origin,actor,name);tx.Commit();return 0;
    });
    public void RevokeSessions(UserIdentity actor,Guid userId,string origin)=>store.SecurityWork(c=>{Require(c,actor,"system.users");using var tx=c.BeginTransaction();Run(c,"UPDATE auth_sessions SET revoked_at=COALESCE(revoked_at,@now) WHERE user_id=@u",("@now",clock().ToUnixTimeSeconds()),("@u",userId.ToString()));Audit(c,"Sessões encerradas",origin,actor,userId.ToString());tx.Commit();return 0;});
}

