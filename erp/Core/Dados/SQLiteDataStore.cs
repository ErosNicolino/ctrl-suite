using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text.Json;

namespace GraficaCaixa;

public sealed partial class DataStore
{
    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    readonly RemoteDataClient? remote;
    bool requireExistingDatabase;
    public bool IsRemote => remote != null;
    public static DataStore Connect(ServerConnection settings)=>new(settings);
    DataStore(ServerConnection settings){remote=new RemoteDataClient(settings);Folder=settings.Url;}
    readonly JsonSerializerOptions json = new() { WriteIndented = true };
    public string Folder { get; private set; }
    public string DatabasePath => Path.Combine(Folder, "caixa_dados.db");
    string LegacyJsonPath => Path.Combine(Folder, "caixa_dados.json");
    string LockFile => Path.Combine(Folder, "caixa_dados.lock");
    string BackupFolder => Path.Combine(Folder, "Backups");
    public DateTime LastChangeUtc => remote!=null?remote.ObservedChangeUtc:new[]{DatabasePath,DatabasePath+"-wal"}.Where(File.Exists).Select(File.GetLastWriteTimeUtc).DefaultIfEmpty(DateTime.MinValue).Max();
    public Task<T> QueryRemoteAsync<T>(string path,CancellationToken cancellation=default)=>remote?.QueryAsync<T>(path,cancellation)??throw new InvalidOperationException("Consulta remota exige conexão.");
    public Task<T> UploadRemoteAsync<T>(string path,object body,CancellationToken cancellation=default)=>remote?.UploadAsync<T>(path,body,cancellation)??throw new InvalidOperationException("Envio remoto exige conexão.");
    public Task<byte[]?> BytesRemoteAsync(string path,CancellationToken cancellation=default)=>remote?.BytesAsync(path,cancellation)??throw new InvalidOperationException("Consulta remota exige conexão.");
    public Task PingRemoteAsync(CancellationToken cancellation=default)=>remote?.PingAsync(cancellation)??throw new InvalidOperationException("Consulta remota exige conexão.");
    public Task<AppData> LoadAsync(CancellationToken cancellation=default)=>remote?.LoadAsync(cancellation)??Task.Run(Load,cancellation);
    public Task<WebCommandResult> RemoteStageAsync(Guid id,WebStageCommand command,CancellationToken cancellation=default)=>remote?.StageAsync(id,command,cancellation)??throw new InvalidOperationException("Comando remoto exige conexão.");

    public DataStore(string folder, bool requireExistingDatabase = false)
    {
        Folder = Path.GetFullPath(folder);
        this.requireExistingDatabase = requireExistingDatabase;
        if (requireExistingDatabase) ValidateRequiredDatabase(); else Directory.CreateDirectory(Folder);
    }
    public void ChangeFolder(string folder) { requireExistingDatabase=false;Directory.CreateDirectory(folder); Folder = folder; EnsureInitialized(); }

    public void EnsureInitialized() { if(remote!=null){return;}if(requireExistingDatabase)ValidateRequiredDatabase();WithLock(() =>
    {
        var databaseExisted = File.Exists(DatabasePath);
        if(databaseExisted) { using var existing=Open(DatabasePath,false); ValidateIntegrity(existing); }
        using (var connection = Open()) { CreateSchema(connection);LegacyCostMigration.Retire(connection,BackupFolder);StageMigration.Apply(connection,BackupFolder);AuditCompaction.Apply(connection,BackupFolder);FiscalDedupe.Apply(connection);LoadRulesUnsafe(connection); }
        if (!databaseExisted && File.Exists(LegacyJsonPath)) MigrateLegacyJsonUnsafe(LegacyJsonPath);
        return 0;
    }); }

    void ValidateRequiredDatabase()
    {
        if(!Directory.Exists(Folder)||!File.Exists(DatabasePath))
            throw new InvalidOperationException($"Não foi possível localizar a base central configurada em:\n{Folder}\n\nO arquivo caixa_dados.db esperado não foi encontrado. O ERP não criará uma nova base automaticamente para evitar perda ou separação de dados.");
    }

    static void ValidateIntegrity(SqliteConnection connection)
    {
        using var cmd=connection.CreateCommand();cmd.CommandText="PRAGMA integrity_check";
        using var reader=cmd.ExecuteReader();while(reader.Read()) if(reader.GetString(0)!="ok") throw new InvalidDataException("O banco apresenta corrupção. Nenhuma gravação foi feita. Use uma cópia recuperada e validada; não sincronize o banco ativo entre os computadores.");
    }
    public bool IsEmpty()
    {
        EnsureInitialized();
        return WithLock(() => { using var c = Open(); return ScalarLong(c, "SELECT COUNT(*) FROM cash_sessions") + ScalarLong(c, "SELECT COUNT(*) FROM scheduled_orders") + ScalarLong(c, "SELECT COUNT(*) FROM lost_entries") + ScalarLong(c, "SELECT COUNT(*) FROM metadata WHERE key IN ('manual_debts','prospects') AND value != '[]'") == 0; });
    }

    public void ImportLegacyJson(string jsonPath) => WithLock(() =>
    {
        if (!File.Exists(jsonPath)) throw new FileNotFoundException("O arquivo JSON selecionado não foi encontrado.", jsonPath);
        using (var c = Open()) CreateSchema(c);
        MigrateLegacyJsonUnsafe(jsonPath); return 0;
    });

    void MigrateLegacyJsonUnsafe(string source)
    {
        var imported = Normalize(JsonSerializer.Deserialize<AppData>(File.ReadAllText(source), json) ?? throw new InvalidDataException("O JSON não contém dados válidos."));
        if (File.Exists(DatabasePath) && new FileInfo(DatabasePath).Length > 0) CreateBackupUnsafe();
        SaveUnsafe(imported);
        Directory.CreateDirectory(BackupFolder);
        File.Copy(source, Path.Combine(BackupFolder, $"caixa_json_original_{DateTime.Now:yyyyMMdd_HHmmss}.json"), true);
    }

    // Senha de acesso: no modo conectado, quem confere é o computador principal.
    public LoginCheck VerifyLogin(string user,string password)=>remote!=null?remote.VerifyLogin(user,password):LoginSecurity.Verify(Load().Prompt12,user,password,"local");
    public bool ResumeSession()=>remote?.ResumeSession()??false;
    public IReadOnlyList<DeviceSummary> Devices()=>remote?.Devices()??throw new InvalidOperationException("Conecte ao servidor.");
    public IReadOnlyList<SessionSummary> Sessions()=>remote?.Sessions()??throw new InvalidOperationException("Conecte ao servidor.");
    public void EndSession(Guid id){if(remote==null)throw new InvalidOperationException();remote.EndSession(id);}
    public void RevokeDevice(Guid id){if(remote==null)throw new InvalidOperationException();remote.RevokeDevice(id);}
    public void RevokePairings(){if(remote==null)throw new InvalidOperationException();remote.RevokePairings();}
    public ServerConnection CreatePairing()=>remote?.CreatePairing()??throw new InvalidOperationException("Conecte ao servidor.");
    public void RotateDevice(){if(remote==null)throw new InvalidOperationException();remote.RotateDevice();}
    public UserIdentity? AuthenticatedUser => remote?.User;
    public Dictionary<string,bool> EffectivePermissions()=>remote?.EffectivePermissions()??throw new InvalidOperationException("Conecte ao servidor.");
    public IReadOnlyList<PermissionState> PermissionPanel(Guid? id,string? role)=>remote?.PermissionPanel(id,role)??throw new InvalidOperationException("Conecte ao servidor.");
    public IReadOnlyList<RoleSummary> ListRoles()=>remote?.ListRoles()??throw new InvalidOperationException("Conecte ao servidor.");
    public void CreateRole(RoleCreate edit){if(remote==null)throw new InvalidOperationException("Conecte ao servidor.");remote.CreateRole(edit);}
    public void DeleteRole(string name){if(remote==null)throw new InvalidOperationException("Conecte ao servidor.");remote.DeleteRole(new RoleDelete(name));}
    public void SavePermissions(PermissionEdit edit){if(remote==null)throw new InvalidOperationException();remote.SavePermissions(edit);}
    public void RevokeSessions(Guid id){if(remote==null)throw new InvalidOperationException();remote.RevokeSessions(id);}
    public IReadOnlyList<AuditEntry> QueryAudit(AuditFilter? filter=null)=>remote?.QueryAudit(filter)??AuditQuery.Run(Load(),filter??new());
    public AppData AuthorizedView(UserIdentity user)=>WithLock(()=>
    {
        using var c=Open();var permissions=Permissions.Resolve(c,user);using var cmd=c.CreateCommand();cmd.CommandText="SELECT owner_until FROM auth_sessions WHERE id=@s";cmd.Parameters.AddWithValue("@s",user.SessionId.ToString());
        return ServerAuthorization.View(ReadUnsafe(c),permissions,Convert.ToInt64(cmd.ExecuteScalar())>DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    });
    public void Logout() => remote?.Logout();
    public IReadOnlyList<UserSummary> ListUsers() => remote?.ListUsers() ?? throw new InvalidOperationException("Conecte ao servidor.");
    public void SaveUser(UserEdit edit) { if(remote==null)throw new InvalidOperationException("Conecte ao servidor.");remote.SaveUser(edit); }
    public void AuthorizeReceipt(ReceiptAuditRequest receipt){if(remote==null)throw new InvalidOperationException("Conecte ao servidor.");remote.AuthorizeReceipt(receipt);}
    public bool OwnerConfigured() => remote?.OwnerConfigured() ?? Load().Prompt12.OwnerPin!=null;
    public void AuthenticateOwner(string pin,bool setup) { if(remote==null)throw new InvalidOperationException("Conecte ao servidor.");remote.AuthenticateOwner(pin,setup); }
    public void ResetOwnerPin(string password,string newPin) { if(remote==null)throw new InvalidOperationException("Conecte ao servidor.");remote.ResetOwnerPin(password,newPin); }
    internal T SecurityWork<T>(Func<SqliteConnection,T> action) => WithLock(()=>{using var c=Open();return action(c);});
    // Leituras sem trava (2026.10.21.3): o que só lê usa conexão somente leitura e uma transação (foto única do banco), sem esperar
    // as gravações. O banco recusa qualquer escrita por essa conexão. Desligar: "LeiturasSemTrava": false no servidor.json.
    public static bool LockFreeReads { get; set; } = true;
    [ThreadStatic] static bool readOnlyScope;
    public T ReadWork<T>(Func<SqliteConnection,T> action)
    {
        if(!LockFreeReads)return SecurityWork(c=>{using var tx=c.BeginTransaction(deferred:true);var result=action(c);tx.Commit();return result;});
        // query_only: mesma proteção de uma conexão somente leitura, sem o problema de abrir banco WAL em modo ReadOnly.
        using var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=DatabasePath,Mode=SqliteOpenMode.ReadWrite,Cache=SqliteCacheMode.Private,Pooling=false}.ToString());c.Open();
        using(var pragma=c.CreateCommand()){pragma.CommandText="PRAGMA busy_timeout=10000; PRAGMA query_only=1;";pragma.ExecuteNonQuery();}
        var previous=readOnlyScope;readOnlyScope=true;
        try{using var tx=c.BeginTransaction(deferred:true);var result=action(c);tx.Commit();return result;}
        finally{readOnlyScope=previous;}
    }
    // Regras e taxas ficam em variáveis globais: só a versão mais nova do banco as troca, para uma leitura com foto antiga
    // não desfazer o que uma gravação acabou de salvar.
    static long globalsVersion=long.MinValue;static readonly object globalsGate=new();
    static void ApplyGlobals(AppData data){lock(globalsGate){if(data.DataVersion<globalsVersion)return;globalsVersion=data.DataVersion;PaymentPolicy.Configure(data.PaymentRates);BusinessRules.Current=data.Rules??=new();}}
    public void ChangeLoginPassword(string user,string current,string next){if(remote!=null)remote.ChangeLoginPassword(user,current,next);else Update(d=>{LoginSecurity.Change(d,user,current,next,Environment.MachineName);return 0;});}
    public AppData Load() { if(remote!=null){var data=remote.Load();PaymentPolicy.Configure(data.PaymentRates);return data;}EnsureInitialized(); return WithLock(ReadUnsafe); }
    public List<BackupInfo> ListBackups() => remote!=null?remote.ListBackups():WithLock(() =>
    {
        Directory.CreateDirectory(BackupFolder);
        return Directory.GetFiles(BackupFolder, "caixa_*").Where(x => Path.GetExtension(x).Equals(".db", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(x).Equals(".json", StringComparison.OrdinalIgnoreCase)).Select(x => new FileInfo(x)).OrderByDescending(x => x.LastWriteTimeUtc).Select(x => new BackupInfo { Path = x.FullName, CreatedAt = x.LastWriteTime, Size = x.Length }).ToList();
    });

    public void RestoreBackup(string backupPath,UserIdentity? actor=null) { if(remote!=null){remote.RestoreBackup(backupPath);return;}WithLock(() =>
    {
        var full = Path.GetFullPath(backupPath); var root = Path.GetFullPath(BackupFolder) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) throw new InvalidOperationException("Backup inválido ou não encontrado.");
        var current = ReadUnsafe(); AppData restored;
        if (Path.GetExtension(full).Equals(".json", StringComparison.OrdinalIgnoreCase)) restored = Normalize(JsonSerializer.Deserialize<AppData>(File.ReadAllText(full), json) ?? throw new InvalidDataException("Backup JSON inválido."));
        else { using var source = Open(full, false); ValidateIntegrity(source); LegacyCostMigration.ArchiveForRead(source,BackupFolder); restored = ReadUnsafe(source); }
        CreateBackupUnsafe();restored.AuditTrail ??= []; foreach (var entry in current.AuditTrail ?? []) if (restored.AuditTrail.All(x => x.Id != entry.Id)) restored.AuditTrail.Add(entry);
        restored.Prompt12.LoginPassword=current.Prompt12.LoginPassword;restored.Prompt12.OwnerPin=current.Prompt12.OwnerPin;restored.DocumentsRoot=current.DocumentsRoot;
        restored.AuditTrail.Add(new AuditEntry { Action = "Backup restaurado", Description = $"Restauração do arquivo {Path.GetFileName(full)}",EntityType="Banco",EntityId="global",UserId=actor?.UserId.ToString()??"",UserLogin=actor?.Login??"",SessionId=actor?.SessionId.ToString()??"",UserName=actor?.Name??"Sistema",Timestamp=DateTime.UtcNow }); restored.DataVersion=current.DataVersion;SaveUnsafe(restored); return 0;
    }); }

    public T Update<T>(Func<AppData, T> action) => remote!=null?remote.Update(action):WithLock(() => { using(var c=Open()) ValidateIntegrity(c); var current = ReadUnsafe();var data=JsonSerializer.Deserialize<AppData>(JsonSerializer.Serialize(current,json),json)!; var result = action(data); data=Normalize(data);FinancialIntegrity.ValidateTransition(current,data);CreateBackupUnsafe(); SaveUnsafe(data); return result; });
    AppData ReadUnsafe() { using var c = Open(); return ReadUnsafe(c); }
    // Desempenho: gravações não precisam do texto antes/depois da auditoria (hoje a maior parte do banco). Lê todos os registros, mas só com o
    // texto de "Cliente excluído" (usado para não recriar cliente apagado). Os registros antigos nunca são regravados (SaveUnsafe só insere novos).
    public AppData LoadForAudit(){EnsureInitialized();return ReadWork(c=>ReadUnsafe(c,true,false,true));}
    public List<AuditEntry> AuditEntriesFull(IEnumerable<Guid> ids)=>ReadWork(c=>{var rows=new List<AuditEntry>();using var cmd=c.CreateCommand();cmd.CommandText=AuditColumns+" WHERE id IN (SELECT value FROM json_each(@ids))";cmd.Parameters.AddWithValue("@ids",JsonSerializer.Serialize(ids.Select(x=>x.ToString())));using var r=cmd.ExecuteReader();while(r.Read())rows.Add(AuditRow(r));return rows;});
    public AuditEntry? AuditEntryFull(Guid id)=>remote!=null?remote.AuditEntryFull(id):AuditEntriesFull([id]).FirstOrDefault();
    AppData ReadForWrite() { using var c = Open(); return ReadUnsafe(c, true, true); }
    // Pesquisa da Auditoria: antes/depois reduzidos ao nome do registro (Descrição/Nome/Número); o completo vem só ao abrir o detalhe.
    const string AuditSummaryColumns="SELECT id,timestamp,action,description,computer,sale_id,user_name,entity_type,entity_id,CASE WHEN json_valid(old_value) AND json_type(old_value)='object' THEN json_object('Number',json_extract(old_value,'$.Number'),'Description',json_extract(old_value,'$.Description'),'Name',json_extract(old_value,'$.Name'),'CustomerName',json_extract(old_value,'$.CustomerName'),'Material',json_extract(old_value,'$.Material'),'Title',json_extract(old_value,'$.Title')) ELSE '' END,CASE WHEN json_valid(new_value) AND json_type(new_value)='object' THEN json_object('Number',json_extract(new_value,'$.Number'),'Description',json_extract(new_value,'$.Description'),'Name',json_extract(new_value,'$.Name'),'CustomerName',json_extract(new_value,'$.CustomerName'),'Material',json_extract(new_value,'$.Material'),'Title',json_extract(new_value,'$.Title'),'Amount',json_extract(new_value,'$.Amount'),'OpeningAmount',json_extract(new_value,'$.OpeningAmount'),'DeclaredClosingAmount',json_extract(new_value,'$.DeclaredClosingAmount')) ELSE '' END,reason,user_id,user_login,session_id FROM audit_trail";
    const string AuditLightColumns="SELECT id,timestamp,action,description,computer,sale_id,user_name,entity_type,entity_id,CASE WHEN action='Cliente excluído' THEN old_value ELSE '' END,CASE WHEN action='Cliente excluído' THEN new_value ELSE '' END,reason,user_id,user_login,session_id FROM audit_trail";
    const string AuditColumns="SELECT id,timestamp,action,description,computer,sale_id,user_name,entity_type,entity_id,old_value,new_value,reason,user_id,user_login,session_id FROM audit_trail";
    static AuditEntry AuditRow(SqliteDataReader r)=>new AuditEntry{Id=G(r,0),Timestamp=D(r,1),Action=S(r,2),Description=S(r,3),Computer=S(r,4),SaleId=GN(r,5),UserName=S(r,6),EntityType=S(r,7),EntityId=S(r,8),OldValue=S(r,9),NewValue=S(r,10),Reason=S(r,11),UserId=S(r,12),UserLogin=S(r,13),SessionId=S(r,14)};
    // Só a auditoria das entidades pedidas (mesmas colunas e ordem da leitura completa).
    static List<AuditEntry> ReadAudit(SqliteConnection c,IEnumerable<string> entityIds){var rows=new List<AuditEntry>();using var cmd=c.CreateCommand();cmd.CommandText=AuditColumns+" WHERE entity_id IN (SELECT value FROM json_each(@ids)) ORDER BY timestamp";cmd.Parameters.AddWithValue("@ids",JsonSerializer.Serialize(entityIds));using var r=cmd.ExecuteReader();while(r.Read())rows.Add(AuditRow(r));return rows;}
    // Desempenho (pacote 11): leitura compartilhada para consultas SOMENTE LEITURA (telas, celular, buscas).
    // Vale enquanto o data_version (lido na mesma transação/conexão) não muda; toda gravação de negócio o incrementa,
    // a mesma regra que os PCs usam para saber quando atualizar a tela. Nunca serve de base para gravação e quem
    // a usa não pode alterar os objetos (copie antes). Sem auditoria e sem segredos (senha/PIN ficam nulos).
    // A leitura também depende do dia (Normalize sincroniza clientes das vendas/pedidos desde ontem): vale só no dia em que foi feita.
    sealed record SharedRead(long Version,DateTime Day,AppData Data);
    SharedRead? sharedRead;
    AppData ReadShared(SqliteConnection c)
    {
        string? stamp;using(var cmd=c.CreateCommand()){cmd.CommandText="SELECT value FROM metadata WHERE key='data_version'";stamp=cmd.ExecuteScalar()?.ToString();}
        var cached=Volatile.Read(ref sharedRead);
        var today=DateTime.Today;if(long.TryParse(stamp,NumberStyles.Integer,Invariant,out var version)&&cached?.Version==version&&cached.Day==today)return cached.Data;
        var data=ReadUnsafe(c,includeAudit:false);data.Prompt12.LoginPassword=null;data.Prompt12.OwnerPin=null;
        if(data.DataVersion==version&&stamp!=null)Volatile.Write(ref sharedRead,new SharedRead(version,today,data));
        return data;
    }
    AppData ReadUnsafe(SqliteConnection c,bool includeAudit=true,bool lightAudit=false,bool summaryAudit=false)
    {
        var data = new AppData { ProductCategories=[], OrderDetailsVersion = 6, ProductCostsVersion=2, FinancialIntegrityVersion=1 }; using (var cmd = c.CreateCommand()) { cmd.CommandText = "SELECT value FROM metadata WHERE key='shared_folder'"; data.SharedFolder = cmd.ExecuteScalar()?.ToString() ?? ""; }
        using (var cmd = c.CreateCommand()) { cmd.CommandText = "SELECT key,value FROM metadata WHERE key IN ('products_v1','product_categories_v1','sale_products_v1','collection_contacts','documents_root','improvement_notes','data_version','budget_rules','manual_debts','prospects','financial_corrections_v1','cash_closure_versions_v1','payment_rates_v1','logo_path','company_profile_v1','backup_schedule_v1','reminders_v1','production_deadlines_v1','calculator_settings_v1','business_rules_v1')"; using var r=cmd.ExecuteReader(); while(r.Read()) { var key=r.GetString(0);if(key=="calculator_settings_v1")data.CalculatorSettingsJson=r.GetString(1);else if(key=="business_rules_v1")data.Rules=ParseRules(r.GetString(1));else if(key=="production_deadlines_v1")data.ProductionDeadlines=JsonSerializer.Deserialize<List<ProductionDeadline>>(r.GetString(1),json)??[];else if(key=="payment_rates_v1")data.PaymentRates=JsonSerializer.Deserialize<Dictionary<string,decimal>>(r.GetString(1),json)??[];else if(key=="logo_path")data.LogoPath=r.GetString(1);else if(key=="company_profile_v1")data.Company=JsonSerializer.Deserialize<CompanyProfile>(r.GetString(1),json)??new();else if(key=="backup_schedule_v1")data.Backup=JsonSerializer.Deserialize<BackupSchedule>(r.GetString(1),json)??new();else if(key=="product_categories_v1")data.ProductCategories=JsonSerializer.Deserialize<List<string>>(r.GetString(1),json)??[];else if(key=="products_v1")data.Products=JsonSerializer.Deserialize<List<Product>>(r.GetString(1),json)??[];else if(key=="sale_products_v1"){}else if(key=="collection_contacts")data.CollectionContacts=JsonSerializer.Deserialize<List<CollectionContact>>(r.GetString(1),json)??[];else if(key=="documents_root")data.DocumentsRoot=r.GetString(1);else if(key=="manual_debts")data.ManualDebts=JsonSerializer.Deserialize<List<ManualDebt>>(r.GetString(1),json)??[];else if(key=="prospects")data.Prospects=JsonSerializer.Deserialize<List<Prospect>>(r.GetString(1),json)??[];else if(key=="financial_corrections_v1")data.FinancialCorrections=JsonSerializer.Deserialize<List<FinancialCorrection>>(r.GetString(1),json)??[];else if(key=="cash_closure_versions_v1")data.CashClosureVersions=JsonSerializer.Deserialize<List<CashClosureVersion>>(r.GetString(1),json)??[];else if(key=="improvement_notes") data.ImprovementNotes=r.GetString(1);else if(key=="budget_rules")data.BudgetRules=JsonSerializer.Deserialize<List<BudgetRule>>(r.GetString(1),json)??[];else if(key=="reminders_v1")data.Reminders=JsonSerializer.Deserialize<List<Reminder>>(r.GetString(1),json)??[];else if(long.TryParse(r.GetString(1),out var v)) data.DataVersion=v; } data.Reminders??=[]; }
        using (var cmd = c.CreateCommand()) { cmd.CommandText = "SELECT value FROM metadata WHERE key='prompt12_v1'"; if (cmd.ExecuteScalar() is string value) data.Prompt12 = JsonSerializer.Deserialize<Prompt12State>(value, json) ?? new Prompt12State(); }
        var sessions = new Dictionary<Guid, CashSession>();
        Read(c, "SELECT id,opened_at,closed_at,opening_amount,declared_closing_amount,opened_by,closed_by FROM cash_sessions ORDER BY opened_at", r => { var x = new CashSession { Id = G(r,0), OpenedAt = D(r,1), ClosedAt = DN(r,2), OpeningAmount = M(r,3), DeclaredClosingAmount = MN(r,4), OpenedBy = S(r,5), ClosedBy = S(r,6) }; sessions[x.Id]=x; data.Sessions.Add(x); });
        Read(c, "SELECT id,session_id,created_at,customer_name,contact,product,units,payment_type,unit_value,total_value FROM sales ORDER BY session_id,sort_index", r => { if(sessions.TryGetValue(G(r,1),out var x)) x.Sales.Add(new Sale{Id=G(r,0),CreatedAt=D(r,2),CustomerName=S(r,3),Contact=S(r,4),Product=S(r,5),Units=r.GetInt32(6),PaymentType=S(r,7),UnitValue=M(r,8),TotalValue=MN(r,9)}); });
        Read(c, "SELECT id,entry_date,customer_name,contact,description,payment_type,amount FROM lost_entries ORDER BY entry_date", r => data.LostEntries.Add(new LostEntry{Id=G(r,0),EntryDate=D(r,1),CustomerName=S(r,2),Contact=S(r,3),Description=S(r,4),PaymentType=S(r,5),Amount=M(r,6)}));
        using(var withdrawals=c.CreateCommand())
        {
            withdrawals.CommandText="SELECT value FROM metadata WHERE key='cash_withdrawals_v1'";
            if(withdrawals.ExecuteScalar() is string value)
                foreach(var entry in JsonSerializer.Deserialize<Dictionary<Guid,List<CashWithdrawal>>>(value,json) ?? [])
                    if(sessions.TryGetValue(entry.Key,out var session))session.Withdrawals=entry.Value??[];
        }
        using(var adjustments=c.CreateCommand())
        {
            adjustments.CommandText="SELECT value FROM metadata WHERE key='cash_adjustments_v1'";
            if(adjustments.ExecuteScalar() is string value)
                foreach(var entry in JsonSerializer.Deserialize<Dictionary<Guid,List<CashAdjustment>>>(value,json) ?? [])
                    if(sessions.TryGetValue(entry.Key,out var session))session.Adjustments=entry.Value??[];
        }
        using(var snapshots=c.CreateCommand()){snapshots.CommandText="SELECT value FROM metadata WHERE key='sale_products_v1'";if(snapshots.ExecuteScalar() is string saved){var products=JsonSerializer.Deserialize<Dictionary<Guid,Sale>>(saved,json)??[];foreach(var sale in data.Sessions.SelectMany(x=>x.Sales))if(products.TryGetValue(sale.Id,out var p)){sale.ProductId=p.ProductId;sale.SuggestedUnitCost=p.SuggestedUnitCost;sale.ProductUnit=p.ProductUnit;sale.Items=p.Items;sale.ItemsTotal=p.ItemsTotal;sale.PaymentGroupId=p.PaymentGroupId;sale.Notes=p.Notes;sale.FeePercent=p.FeePercent;}}}
        var orders = new Dictionary<Guid, ScheduledOrder>();
        Read(c, "SELECT id,order_number,created_at,due_date,artwork_approved_at,production_business_days,customer_name,contact,description,stage,order_total,deposit_paid,deposit_amount,deposit_payment_type,deposit_sale_id,fully_paid,final_payment_type,final_sale_id,pay_after_delivery,payment_received_at,sent_to_production_at,is_uncollected,archived_as_uncollected_at,notes,price_pending,pdf_generated_at,revision FROM scheduled_orders ORDER BY order_number", r => { var x=new ScheduledOrder{Id=G(r,0),Number=r.GetInt64(1),CreatedAt=D(r,2),DueDate=D(r,3),ArtworkApprovedAt=DN(r,4),ProductionBusinessDays=r.GetInt32(5),CustomerName=S(r,6),Contact=S(r,7),Description=S(r,8),Stage=S(r,9),OrderTotal=M(r,10),DepositPaid=B(r,11),DepositAmount=M(r,12),DepositPaymentType=S(r,13),DepositSaleId=GN(r,14),FullyPaid=B(r,15),FinalPaymentType=S(r,16),FinalSaleId=GN(r,17),PayAfterDelivery=B(r,18),PaymentReceivedAt=DN(r,19),SentToProductionAt=DN(r,20),IsUncollected=B(r,21),ArchivedAsUncollectedAt=DN(r,22),Notes=S(r,23),PricePending=B(r,24),PdfGeneratedAt=DN(r,25),Revision=r.GetInt64(26)}; orders[x.Id]=x; data.ScheduledOrders.Add(x); });
        Read(c, "SELECT id,order_id,quantity,product FROM order_items ORDER BY order_id,sort_index", r => { if(orders.TryGetValue(G(r,1),out var o))o.Items.Add(new OrderItem{Id=G(r,0),Quantity=r.GetInt32(2),Product=S(r,3)}); });
        Dictionary<Guid,OrderDetails> orderDetails=[]; // lido e interpretado uma vez só (antes eram duas por leitura)
        using(var details=c.CreateCommand())
        {
            details.CommandText="SELECT value FROM metadata WHERE key='order_details_v1'";
            if(details.ExecuteScalar() is string value)orderDetails=JsonSerializer.Deserialize<Dictionary<Guid,OrderDetails>>(value,json) ?? [];
            foreach(var entry in orderDetails)if(orders.TryGetValue(entry.Key,out var order))entry.Value.Apply(order);
        }
        Read(c, "SELECT id,order_id,paid_at,amount,payment_type,sale_id,note FROM order_payments ORDER BY order_id,sort_index", r => { if(orders.TryGetValue(G(r,1),out var o))o.Payments.Add(new OrderPayment{Id=G(r,0),PaidAt=D(r,2),Amount=M(r,3),PaymentType=S(r,4),SaleId=GN(r,5),Note=S(r,6)}); });
        foreach(var entry in orderDetails)if(orders.TryGetValue(entry.Key,out var paymentOrder))entry.Value.ApplyPayments(paymentOrder);
        Read(c, "SELECT id,name,contact,email,cpf_cnpj,address,notes,updated_at,price_type,merged_names FROM customers ORDER BY name", r => data.Customers.Add(new CustomerProfile{Id=G(r,0),Name=S(r,1),Contact=S(r,2),Email=S(r,3),CpfCnpj=S(r,4),Address=S(r,5),Notes=S(r,6),UpdatedAt=D(r,7),PriceType=S(r,8),MergedNames=S(r,9) is {Length:>0} merged?merged:null}));
        if(includeAudit)Read(c, (summaryAudit?AuditSummaryColumns:lightAudit?AuditLightColumns:AuditColumns)+" ORDER BY timestamp", r => data.AuditTrail.Add(AuditRow(r))); ApplyGlobals(data);return Normalize(data);
    }

    public bool CommitRemote(CommitRequest request, UserIdentity? identity = null) => WithLock(() =>
    {
        if(remote!=null) throw new InvalidOperationException("Apenas o computador principal pode confirmar operações.");
        if(identity==null)throw new SessionInvalidException();
        var payloadHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(ServerAuthorization.Json(request))));
        Dictionary<string,bool> permissions;
        using(var connection=Open())
        {
            ValidateIntegrity(connection);permissions=Permissions.Resolve(connection,identity);
            using var receipt=connection.CreateCommand();receipt.CommandText="SELECT actor_id,payload_hash FROM remote_operations WHERE id=@id";receipt.Parameters.AddWithValue("@id",request.OperationId.ToString());
            using var r=receipt.ExecuteReader();if(r.Read()){if(r.GetString(0)!=identity.UserId.ToString()||r.GetString(1)!=payloadHash&&r.GetString(1)!=ServerAuthorization.CompatibleLegacyCommitHash(request))throw new InvalidOperationException("Identificador de operação já utilizado.");return true;}
        }
        if(request.ClientOrderDetailsVersion<9 || request.Data.ProductCostsVersion<2)throw new InvalidOperationException("Atualize o ERP deste computador para 2026.10.7.1 antes de salvar. Esta versão preserva a leitura por usuário e a composição financeira do recebimento.");
        var current=ReadUnsafe();if(current.DataVersion!=request.ExpectedVersion)return false;
        request.Data.ProductCategories??=current.ProductCategories;
        // Computador com versão antiga não conhece os lembretes: mantém os que já estão no banco.
        request.Data.Reminders??=current.Reminders;request.Data.ProductionDeadlines??=current.ProductionDeadlines;
        var ownerUnlocked=false;
        if(identity!=null)
        {
            using var ownerConnection=Open();using var owner=ownerConnection.CreateCommand();
            owner.CommandText="SELECT COUNT(*) FROM auth_sessions WHERE id=@id AND revoked_at IS NULL AND owner_until>@now";owner.Parameters.AddWithValue("@id",identity.SessionId.ToString());owner.Parameters.AddWithValue("@now",DateTimeOffset.UtcNow.ToUnixTimeSeconds());ownerUnlocked=Convert.ToInt64(owner.ExecuteScalar())==1;
            if(ownerUnlocked){owner.CommandText="UPDATE auth_sessions SET owner_until=@until WHERE id=@id";owner.Parameters.AddWithValue("@until",DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds());owner.ExecuteNonQuery();}
        }
        ServerAuthorization.MergeHidden(current,request.Data,permissions,ownerUnlocked && permissions.GetValueOrDefault("system.owner"));
        if(current.Products.Any(x=>x.ExactQuantityPrices.Count>0)&&current.Products.Any(x=>x.ExactQuantityPrices.Count>0&&request.Data.Products.FirstOrDefault(p=>p.Id==x.Id) is Product incomingProduct&&incomingProduct.ExactQuantityPrices.Count==0))throw new InvalidOperationException("Atualize o ERP deste computador para preservar os preços por quantidade exata.");
        if(request.Data.SettingsVersion<1)throw new InvalidOperationException("Atualize o ERP dos dois computadores para preservar as configurações e descontos em reais.");
        if(request.OperationId==Guid.Empty)throw new InvalidOperationException("Operação inválida.");
        if(request.Data.FollowUpVersion<2)throw new InvalidOperationException("Atualize o ERP deste computador para preservar Devedores e Possíveis pedidos.");
        request.Data.DataVersion=current.DataVersion;
        foreach(var payment in request.Data.ScheduledOrders.SelectMany(x=>x.Payments).Where(x=>current.ScheduledOrders.SelectMany(o=>o.Payments).All(y=>y.Id!=x.Id)))payment.OperationId=request.OperationId;
        ServerAuthorization.ValidateAndAudit(current,request.Data,identity!,permissions);
        var incoming=Normalize(request.Data);
        FinancialIntegrity.ValidateTransition(current,incoming);
        incoming.AuditTrail.Add(new AuditEntry{UserId=identity!.UserId.ToString(),UserLogin=identity.Login,SessionId=identity.SessionId.ToString(),UserName=identity.Name,Timestamp=DateTime.UtcNow,Computer="Servidor",Action="Operação remota validada",EntityType="Banco",EntityId=request.OperationId.ToString(),OldValue=current.DataVersion.ToString(),NewValue=(current.DataVersion+1).ToString()});
        CreateBackupUnsafe();SaveUnsafe(incoming,request.OperationId,identity.UserId.ToString(),payloadHash);return true;
    });
    // Desempenho: uma gravação repete os mesmos ~10 comandos SQL milhares de vezes; cada um é preparado uma vez só por gravação.
    void SaveUnsafe(AppData data, Guid? operationId=null, string actorId="",string payloadHash="",string? mobileResult=null)
    {
        prepared=new();
        try{SaveCore(data,operationId,actorId,payloadHash,mobileResult);}
        finally{foreach(var kept in prepared.Values)kept.Dispose();prepared=null;}
    }
    [ThreadStatic] static Dictionary<string,SqliteCommand>? prepared;
    void SaveCore(AppData data, Guid? operationId, string actorId,string payloadHash,string? mobileResult)
    {
        using var c=Open(); using var tx=c.BeginTransaction();
        Execute(c,tx,"INSERT INTO metadata(key,value) VALUES('prompt12_v1',@v) ON CONFLICT(key) DO UPDATE SET value=excluded.value",("@v",JsonSerializer.Serialize(data.Prompt12,json)));
        Execute(c,tx,"INSERT INTO metadata(key,value) VALUES('payment_rates_v1',@v) ON CONFLICT(key) DO UPDATE SET value=excluded.value",("@v",JsonSerializer.Serialize(data.PaymentRates,json)));
        Execute(c,tx,"INSERT INTO metadata(key,value) VALUES('logo_path',@v) ON CONFLICT(key) DO UPDATE SET value=excluded.value",("@v",data.LogoPath));
        Execute(c,tx,"INSERT INTO metadata(key,value) VALUES('company_profile_v1',@v) ON CONFLICT(key) DO UPDATE SET value=excluded.value",("@v",JsonSerializer.Serialize(data.Company??new(),json)));
        Execute(c,tx,"INSERT INTO metadata(key,value) VALUES('backup_schedule_v1',@v) ON CONFLICT(key) DO UPDATE SET value=excluded.value",("@v",JsonSerializer.Serialize(data.Backup??new(),json)));
        // Vazio = nunca configurado: não cria a chave, e a calculadora segue nos padrões oficiais.
        if(data.Rules!=null){Execute(c,tx,"INSERT INTO metadata(key,value) VALUES('business_rules_v1',@v) ON CONFLICT(key) DO UPDATE SET value=excluded.value",("@v",JsonSerializer.Serialize(data.Rules,json)));BusinessRules.Current=data.Rules;}
        if(!string.IsNullOrEmpty(data.CalculatorSettingsJson))Execute(c,tx,"INSERT INTO metadata(key,value) VALUES('calculator_settings_v1',@v) ON CONFLICT(key) DO UPDATE SET value=excluded.value",("@v",data.CalculatorSettingsJson));
        Execute(c,tx,"INSERT INTO metadata(key,value) VALUES('shared_folder',@v) ON CONFLICT(key) DO UPDATE SET value=excluded.value",("@v",data.SharedFolder));
        Execute(c,tx,"INSERT INTO metadata(key,value) VALUES('collection_contacts',@v) ON CONFLICT(key) DO UPDATE SET value=excluded.value",("@v",JsonSerializer.Serialize(data.CollectionContacts,json)));
        Execute(c,tx,"INSERT INTO metadata(key,value) VALUES('documents_root',@v) ON CONFLICT(key) DO UPDATE SET value=excluded.value",("@v",data.DocumentsRoot));
        Execute(c,tx,"INSERT INTO metadata(key,value) VALUES('improvement_notes',@v) ON CONFLICT(key) DO UPDATE SET value=excluded.value",("@v",data.ImprovementNotes));
        Execute(c,tx,"INSERT INTO metadata(key,value) VALUES('budget_rules',@v) ON CONFLICT(key) DO UPDATE SET value=excluded.value",("@v",JsonSerializer.Serialize(data.BudgetRules,json)));
                Execute(c,tx,"INSERT INTO metadata(key,value) VALUES('manual_debts',@v) ON CONFLICT(key) DO UPDATE SET value=excluded.value",("@v",JsonSerializer.Serialize(data.ManualDebts,json)));
        Execute(c,tx,"INSERT INTO metadata(key,value) VALUES('prospects',@v) ON CONFLICT(key) DO UPDATE SET value=excluded.value",("@v",JsonSerializer.Serialize(data.Prospects,json)));
        Execute(c,tx,"INSERT INTO metadata(key,value) VALUES('production_deadlines_v1',@v) ON CONFLICT(key) DO UPDATE SET value=excluded.value",("@v",JsonSerializer.Serialize(data.ProductionDeadlines??ProductionDeadlineDefaults.Create(),json)));
        Execute(c,tx,"INSERT INTO metadata(key,value) VALUES('reminders_v1',@v) ON CONFLICT(key) DO UPDATE SET value=excluded.value",("@v",JsonSerializer.Serialize(data.Reminders??[],json)));
        Execute(c,tx,"INSERT INTO metadata(key,value) VALUES('financial_corrections_v1',@v) ON CONFLICT(key) DO UPDATE SET value=excluded.value",("@v",JsonSerializer.Serialize(data.FinancialCorrections,json)));
        Execute(c,tx,"INSERT INTO metadata(key,value) VALUES('cash_closure_versions_v1',@v) ON CONFLICT(key) DO UPDATE SET value=excluded.value",("@v",JsonSerializer.Serialize(data.CashClosureVersions,json)));
        Execute(c,tx,"INSERT INTO metadata(key,value) VALUES('order_details_v1',@v) ON CONFLICT(key) DO UPDATE SET value=excluded.value",
            ("@v",JsonSerializer.Serialize(data.ScheduledOrders.ToDictionary(x=>x.Id,x=>new OrderDetails(x)),json)));
        Execute(c,tx,"INSERT INTO metadata(key,value) VALUES('cash_withdrawals_v1',@v) ON CONFLICT(key) DO UPDATE SET value=excluded.value",
            ("@v",JsonSerializer.Serialize(data.Sessions.ToDictionary(x=>x.Id,x=>x.Withdrawals),json)));
        Execute(c,tx,"INSERT INTO metadata(key,value) VALUES('cash_adjustments_v1',@v) ON CONFLICT(key) DO UPDATE SET value=excluded.value",
            ("@v",JsonSerializer.Serialize(data.Sessions.Where(x=>x.Adjustments.Count>0).ToDictionary(x=>x.Id,x=>x.Adjustments),json)));
        Execute(c,tx,"INSERT INTO metadata(key,value) VALUES('product_categories_v1',@v) ON CONFLICT(key) DO UPDATE SET value=excluded.value",("@v",JsonSerializer.Serialize(data.ProductCategories??[],json)));
        Execute(c,tx,"INSERT INTO metadata(key,value) VALUES('products_v1',@v) ON CONFLICT(key) DO UPDATE SET value=excluded.value",("@v",JsonSerializer.Serialize(data.Products,json)));
        Execute(c,tx,"INSERT INTO metadata(key,value) VALUES('sale_products_v1',@v) ON CONFLICT(key) DO UPDATE SET value=excluded.value",("@v",JsonSerializer.Serialize(data.Sessions.SelectMany(x=>x.Sales).Where(x=>x.ProductId!=null||x.Items.Count>0||x.ItemsTotal!=null||x.PaymentGroupId!=null||x.Notes.Length>0||x.FeePercent!=null).ToDictionary(x=>x.Id),json)));
        data.DataVersion++; Execute(c,tx,"INSERT INTO metadata(key,value) VALUES('data_version',@v) ON CONFLICT(key) DO UPDATE SET value=excluded.value",("@v",data.DataVersion.ToString(Invariant)));
        var keepSessions=new List<string>();var keepSales=new List<string>();
        foreach(var x in data.Sessions){keepSessions.Add(x.Id.ToString());Execute(c,tx,"INSERT INTO cash_sessions VALUES(@id,@opened,@closed,@opening,@declared,@by,@closedby) ON CONFLICT(id) DO UPDATE SET opened_at=excluded.opened_at,closed_at=excluded.closed_at,opening_amount=excluded.opening_amount,declared_closing_amount=excluded.declared_closing_amount,opened_by=excluded.opened_by,closed_by=excluded.closed_by",("@id",x.Id),("@opened",T(x.OpenedAt)),("@closed",TN(x.ClosedAt)),("@opening",N(x.OpeningAmount)),("@declared",NN(x.DeclaredClosingAmount)),("@by",x.OpenedBy),("@closedby",x.ClosedBy));for(var i=0;i<x.Sales.Count;i++){var s=x.Sales[i];keepSales.Add(s.Id.ToString());Execute(c,tx,"INSERT INTO sales VALUES(@id,@session,@sort,@created,@customer,@contact,@product,@units,@payment,@unit,@total) ON CONFLICT(id) DO UPDATE SET session_id=excluded.session_id,sort_index=excluded.sort_index,created_at=excluded.created_at,customer_name=excluded.customer_name,contact=excluded.contact,product=excluded.product,units=excluded.units,payment_type=excluded.payment_type,unit_value=excluded.unit_value,total_value=excluded.total_value",("@id",s.Id),("@session",x.Id),("@sort",i),("@created",T(s.CreatedAt)),("@customer",s.CustomerName),("@contact",s.Contact),("@product",s.Product),("@units",s.Units),("@payment",s.PaymentType),("@unit",N(s.UnitValue)),("@total",NN(s.TotalValue)));}}
        DeleteMissing(c,tx,"cash_sessions",keepSessions);DeleteMissing(c,tx,"sales",keepSales);DeleteOrphans(c,tx,"sales","session_id","cash_sessions");ReplaceSimple(c,tx,"lost_entries",data.LostEntries.Select(x=>(x.Id,(object?[])[T(x.EntryDate),x.CustomerName,x.Contact,x.Description,x.PaymentType,N(x.Amount)])).ToList(),"entry_date,customer_name,contact,description,payment_type,amount");
        var keepOrders=new List<string>();
        foreach(var x in data.ScheduledOrders){if(x.Number<=0){using var number=c.CreateCommand();number.Transaction=tx;number.CommandText="SELECT COALESCE(MAX(order_number),0)+1 FROM scheduled_orders";x.Number=Convert.ToInt64(number.ExecuteScalar());}keepOrders.Add(x.Id.ToString());Execute(c,tx,"INSERT INTO scheduled_orders(id,order_number,created_at,due_date,artwork_approved_at,production_business_days,customer_name,contact,description,stage,order_total,deposit_paid,deposit_amount,deposit_payment_type,deposit_sale_id,fully_paid,final_payment_type,final_sale_id,pay_after_delivery,payment_received_at,sent_to_production_at,is_uncollected,archived_as_uncollected_at,notes,price_pending,pdf_generated_at,revision) VALUES(@id,@number,@created,@due,@approved,@days,@customer,@contact,@description,@stage,@total,@depositpaid,@deposit,@deposittype,@depositsale,@fullypaid,@finaltype,@finalsale,@after,@received,@production,@uncollected,@archived,@notes,@pending,@pdf,@revision) ON CONFLICT(id) DO UPDATE SET order_number=excluded.order_number,created_at=excluded.created_at,due_date=excluded.due_date,artwork_approved_at=excluded.artwork_approved_at,production_business_days=excluded.production_business_days,customer_name=excluded.customer_name,contact=excluded.contact,description=excluded.description,stage=excluded.stage,order_total=excluded.order_total,deposit_paid=excluded.deposit_paid,deposit_amount=excluded.deposit_amount,deposit_payment_type=excluded.deposit_payment_type,deposit_sale_id=excluded.deposit_sale_id,fully_paid=excluded.fully_paid,final_payment_type=excluded.final_payment_type,final_sale_id=excluded.final_sale_id,pay_after_delivery=excluded.pay_after_delivery,payment_received_at=excluded.payment_received_at,sent_to_production_at=excluded.sent_to_production_at,is_uncollected=excluded.is_uncollected,archived_as_uncollected_at=excluded.archived_as_uncollected_at,notes=excluded.notes,price_pending=excluded.price_pending,pdf_generated_at=excluded.pdf_generated_at,revision=excluded.revision",("@id",x.Id),("@number",x.Number),("@created",T(x.CreatedAt)),("@due",T(x.DueDate)),("@approved",TN(x.ArtworkApprovedAt)),("@days",x.ProductionBusinessDays),("@customer",x.CustomerName),("@contact",x.Contact),("@description",x.Description),("@stage",x.Stage),("@total",N(x.OrderTotal)),("@depositpaid",x.DepositPaid),("@deposit",N(x.DepositAmount)),("@deposittype",x.DepositPaymentType),("@depositsale",x.DepositSaleId),("@fullypaid",x.FullyPaid),("@finaltype",x.FinalPaymentType),("@finalsale",x.FinalSaleId),("@after",x.PayAfterDelivery),("@received",TN(x.PaymentReceivedAt)),("@production",TN(x.SentToProductionAt)),("@uncollected",x.IsUncollected),("@archived",TN(x.ArchivedAsUncollectedAt)),("@notes",x.Notes),("@pending",x.PricePending),("@pdf",TN(x.PdfGeneratedAt)),("@revision",x.Revision));Execute(c,tx,"DELETE FROM order_items WHERE order_id=@id",("@id",x.Id));for(var i=0;i<x.Items.Count;i++){var item=x.Items[i];Execute(c,tx,"INSERT INTO order_items VALUES(@id,@order,@sort,@quantity,@product)",( "@id",item.Id),("@order",x.Id),("@sort",i),("@quantity",item.Quantity),("@product",item.Product));}Execute(c,tx,"DELETE FROM order_payments WHERE order_id=@id",("@id",x.Id));for(var i=0;i<x.Payments.Count;i++){var p=x.Payments[i];Execute(c,tx,"INSERT INTO order_payments VALUES(@id,@order,@sort,@paid,@amount,@type,@sale,@note)",( "@id",p.Id),("@order",x.Id),("@sort",i),("@paid",T(p.PaidAt)),("@amount",N(p.Amount)),("@type",p.PaymentType),("@sale",p.SaleId),("@note",p.Note));}}
        DeleteMissing(c,tx,"scheduled_orders",keepOrders);DeleteOrphans(c,tx,"order_items","order_id","scheduled_orders");DeleteOrphans(c,tx,"order_payments","order_id","scheduled_orders");ReplaceSimple(c,tx,"customers",data.Customers.Select(x=>(x.Id,(object?[])[x.Name,x.Contact,x.Email,x.CpfCnpj,x.Address,x.Notes,T(x.UpdatedAt),x.PriceType??"",x.MergedNames??""])).ToList(),"name,contact,email,cpf_cnpj,address,notes,updated_at,price_type,merged_names");InsertNewAudit(c,tx,data.AuditTrail);if(operationId is Guid op) Execute(c,tx,"INSERT INTO remote_operations(id,actor_id,payload_hash) VALUES(@id,@actor,@hash)",("@id",op),("@actor",actorId),("@hash",payloadHash));if(operationId is Guid resultOp && mobileResult!=null)Execute(c,tx,"INSERT INTO metadata(key,value) VALUES(@key,@result)",("@key","mobile_result_"+resultOp.ToString("N")),("@result",mobileResult));
        // Recibos de operação só servem para responder a um reenvio recente: guarda os 1000 últimos (antes cresciam para sempre).
        Execute(c,tx,"DELETE FROM remote_operations WHERE rowid NOT IN (SELECT rowid FROM remote_operations ORDER BY rowid DESC LIMIT 1000)");
        Execute(c,tx,"DELETE FROM metadata WHERE key LIKE 'mobile\\_result\\_%' ESCAPE '\\' AND substr(key,15) NOT IN (SELECT replace(id,'-','') FROM remote_operations)");
        tx.Commit();ApplyGlobals(data);
    }

    static AppData Normalize(AppData data)
    {
        data.ProductionDeadlines??=ProductionDeadlineDefaults.Create();
        data.Rules??=new();if(!readOnlyScope)BusinessRules.Current=data.Rules;
        data.Prompt12??=new Prompt12State();data.Prompt12.Losses??=[];data.Prompt12.Expenses??=[];data.Prompt12.PurchaseNeeds??=[];data.Prompt12.Purchases??=[];data.Prompt12.Suppliers??=[];data.Prompt12.RetiredProfessionalQuotes??=[];data.Prompt12.Materials??=[];data.Prompt12.KnownYields??=[];data.Prompt12.FiscalDocuments??=[];data.Prompt12.Reworks??=[];data.Prompt12.Outsourced??=[];data.Prompt12.CommunicationTemplates??=[];
        data.SettingsVersion=1;data.PaymentRates??=[];data.ProductCostsVersion=2;data.Products??=[];data.OrderDetailsVersion=7;data.FinancialIntegrityVersion=1;data.FollowUpVersion=2;data.CollectionContacts??=[];data.ManualDebts??=[];data.Prospects??=[];data.Sessions??=[];data.LostEntries??=[];data.ScheduledOrders??=[];data.Customers??=[];data.AuditTrail??=[];data.FinancialCorrections??=[];data.CashClosureVersions??=[];data.BudgetRules??=[];if(data.BudgetRules.Count==0)data.BudgetRules=BudgetDefaults.Create();foreach(var s in data.Sessions){s.Sales??=[];s.Withdrawals??=[];}long next=data.ScheduledOrders.Where(x=>x.Number>0).Select(x=>x.Number).DefaultIfEmpty().Max()+1;
        foreach(var order in data.ScheduledOrders.OrderBy(x=>x.CreatedAt)){order.Payments??=[];order.Items??=[];if(order.Number<=0)order.Number=next++;if(order.ProductionBusinessDays is <0 or >BusinessRules.MaxDeadline)order.ProductionBusinessDays=BusinessRules.Current.DefaultDeadline;if(order.Items.Count==0&&!string.IsNullOrWhiteSpace(order.Description))order.Items.Add(new OrderItem{Quantity=1,Product=order.Description});order.Description=order.Items.Count>0?string.Join(" + ",order.Items.Select(x=>$"{x.Quantity} {x.Product}")):order.Description;if(order.Payments.Count==0){var sales=data.Sessions.SelectMany(x=>x.Sales).ToList();if(order.DepositSaleId is Guid depositId){var sale=sales.FirstOrDefault(x=>x.Id==depositId);order.Payments.Add(new OrderPayment{Amount=sale?.Total??order.DepositAmount,PaymentType=sale?.PaymentType??order.DepositPaymentType,PaidAt=sale?.CreatedAt??order.CreatedAt,SaleId=depositId,Note="Sinal"});}if(order.FinalSaleId is Guid finalId&&finalId!=order.DepositSaleId){var sale=sales.FirstOrDefault(x=>x.Id==finalId);order.Payments.Add(new OrderPayment{Amount=sale?.Total??Math.Max(0,order.OrderTotal-order.DepositAmount),PaymentType=sale?.PaymentType??order.FinalPaymentType,PaidAt=sale?.CreatedAt??order.PaymentReceivedAt??order.CreatedAt,SaleId=finalId,Note="Pagamento final"});}}}
        data.OrderDetailsVersion=9;
        var paymentSales=data.Sessions.SelectMany(x=>x.Sales).ToDictionary(x=>x.Id);
        foreach(var payment in data.ScheduledOrders.SelectMany(x=>x.Payments))if(payment.SaleId is Guid saleId && paymentSales.TryGetValue(saleId,out var sale))payment.FeePercent=sale.FeePercent;
        // Parcelas gravadas antes do número do pedido existir saíram como "… do pedido #0:". Corrige só o texto, pelo vínculo pagamento → lançamento (idempotente).
        foreach(var order in data.ScheduledOrders.Where(x=>x.Number>0))foreach(var payment in order.Payments)if(payment.SaleId is Guid id&&paymentSales.TryGetValue(id,out var parcel)&&parcel.Product.Contains(" do pedido #0:",StringComparison.Ordinal))parcel.Product=parcel.Product.Replace(" do pedido #0:",$" do pedido #{order.Number}:");
        CustomerBasicOperations.SyncFromRecords(data,DateTime.Today.AddDays(-1));return data;
    }
    SqliteConnection Open()=>Open(DatabasePath,true);static SqliteConnection Open(string path,bool write){var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=write?SqliteOpenMode.ReadWriteCreate:SqliteOpenMode.ReadOnly,Cache=SqliteCacheMode.Private,Pooling=false}.ToString());c.Open();if(write){using var cmd=c.CreateCommand();cmd.CommandText="PRAGMA foreign_keys=ON; PRAGMA journal_mode=WAL; PRAGMA busy_timeout=10000;";cmd.ExecuteNonQuery();}return c;}
    static void CreateSchema(SqliteConnection c){using var cmd=c.CreateCommand();cmd.CommandText="""
CREATE TABLE IF NOT EXISTS remote_operations(id TEXT PRIMARY KEY);
CREATE TABLE IF NOT EXISTS metadata(key TEXT PRIMARY KEY,value TEXT NOT NULL);CREATE TABLE IF NOT EXISTS cash_sessions(id TEXT PRIMARY KEY,opened_at TEXT NOT NULL,closed_at TEXT,opening_amount TEXT NOT NULL,declared_closing_amount TEXT,opened_by TEXT NOT NULL,closed_by TEXT NOT NULL);CREATE TABLE IF NOT EXISTS sales(id TEXT PRIMARY KEY,session_id TEXT NOT NULL,sort_index INTEGER NOT NULL,created_at TEXT NOT NULL,customer_name TEXT NOT NULL,contact TEXT NOT NULL,product TEXT NOT NULL,units INTEGER NOT NULL,payment_type TEXT NOT NULL,unit_value TEXT NOT NULL,total_value TEXT,FOREIGN KEY(session_id) REFERENCES cash_sessions(id) ON DELETE CASCADE);CREATE INDEX IF NOT EXISTS ix_sales_session ON sales(session_id,sort_index);CREATE TABLE IF NOT EXISTS lost_entries(id TEXT PRIMARY KEY,entry_date TEXT NOT NULL,customer_name TEXT NOT NULL,contact TEXT NOT NULL,description TEXT NOT NULL,payment_type TEXT NOT NULL,amount TEXT NOT NULL);CREATE TABLE IF NOT EXISTS scheduled_orders(id TEXT PRIMARY KEY,order_number INTEGER NOT NULL UNIQUE,created_at TEXT NOT NULL,due_date TEXT NOT NULL,artwork_approved_at TEXT,production_business_days INTEGER NOT NULL,customer_name TEXT NOT NULL,contact TEXT NOT NULL,description TEXT NOT NULL,stage TEXT NOT NULL,order_total TEXT NOT NULL,deposit_paid INTEGER NOT NULL,deposit_amount TEXT NOT NULL,deposit_payment_type TEXT NOT NULL,deposit_sale_id TEXT,fully_paid INTEGER NOT NULL,final_payment_type TEXT NOT NULL,final_sale_id TEXT,pay_after_delivery INTEGER NOT NULL,payment_received_at TEXT,sent_to_production_at TEXT,is_uncollected INTEGER NOT NULL,archived_as_uncollected_at TEXT,notes TEXT NOT NULL);CREATE INDEX IF NOT EXISTS ix_orders_due ON scheduled_orders(due_date);CREATE INDEX IF NOT EXISTS ix_orders_customer ON scheduled_orders(customer_name);CREATE INDEX IF NOT EXISTS ix_orders_stage ON scheduled_orders(stage);CREATE TABLE IF NOT EXISTS order_items(id TEXT PRIMARY KEY,order_id TEXT NOT NULL,sort_index INTEGER NOT NULL,quantity INTEGER NOT NULL,product TEXT NOT NULL,FOREIGN KEY(order_id) REFERENCES scheduled_orders(id) ON DELETE CASCADE);CREATE TABLE IF NOT EXISTS order_payments(id TEXT PRIMARY KEY,order_id TEXT NOT NULL,sort_index INTEGER NOT NULL,paid_at TEXT NOT NULL,amount TEXT NOT NULL,payment_type TEXT NOT NULL,sale_id TEXT,note TEXT NOT NULL,FOREIGN KEY(order_id) REFERENCES scheduled_orders(id) ON DELETE CASCADE);CREATE TABLE IF NOT EXISTS customers(id TEXT PRIMARY KEY,name TEXT NOT NULL,contact TEXT NOT NULL,notes TEXT NOT NULL,updated_at TEXT NOT NULL);CREATE INDEX IF NOT EXISTS ix_customers_name ON customers(name);CREATE TABLE IF NOT EXISTS audit_trail(id TEXT PRIMARY KEY,timestamp TEXT NOT NULL,action TEXT NOT NULL,description TEXT NOT NULL,computer TEXT NOT NULL,sale_id TEXT);CREATE INDEX IF NOT EXISTS ix_audit_timestamp ON audit_trail(timestamp);
""";cmd.ExecuteNonQuery();
        var columns = new HashSet<string>(); using(var info=c.CreateCommand()) { info.CommandText="PRAGMA table_info(scheduled_orders)"; using var r=info.ExecuteReader(); while(r.Read()) columns.Add(r.GetString(1)); }
        foreach(var addition in new[]{("price_pending","INTEGER NOT NULL DEFAULT 0"),("pdf_generated_at","TEXT"),("revision","INTEGER NOT NULL DEFAULT 0")}) if(!columns.Contains(addition.Item1)){using var alter=c.CreateCommand();alter.CommandText=$"ALTER TABLE scheduled_orders ADD COLUMN {addition.Item1} {addition.Item2}";alter.ExecuteNonQuery();}
        var customerColumns = new HashSet<string>(); using(var info=c.CreateCommand()) { info.CommandText="PRAGMA table_info(customers)"; using var r=info.ExecuteReader(); while(r.Read()) customerColumns.Add(r.GetString(1)); }
        foreach(var addition in new[]{("email","TEXT NOT NULL DEFAULT ''"),("cpf_cnpj","TEXT NOT NULL DEFAULT ''"),("address","TEXT NOT NULL DEFAULT ''"),("price_type","TEXT NOT NULL DEFAULT ''"),("merged_names","TEXT NOT NULL DEFAULT ''")}) if(!customerColumns.Contains(addition.Item1)){using var alter=c.CreateCommand();alter.CommandText=$"ALTER TABLE customers ADD COLUMN {addition.Item1} {addition.Item2}";alter.ExecuteNonQuery();}
        var auditColumns=new HashSet<string>();using(var info=c.CreateCommand()){info.CommandText="PRAGMA table_info(audit_trail)";using var r=info.ExecuteReader();while(r.Read())auditColumns.Add(r.GetString(1));}
        foreach(var addition in new[]{("user_name","TEXT NOT NULL DEFAULT 'Sistema'"),("entity_type","TEXT NOT NULL DEFAULT ''"),("entity_id","TEXT NOT NULL DEFAULT ''"),("old_value","TEXT NOT NULL DEFAULT ''"),("new_value","TEXT NOT NULL DEFAULT ''"),("reason","TEXT NOT NULL DEFAULT ''"),("user_id","TEXT NOT NULL DEFAULT ''"),("user_login","TEXT NOT NULL DEFAULT ''"),("session_id","TEXT NOT NULL DEFAULT ''")})if(!auditColumns.Contains(addition.Item1)){using var alter=c.CreateCommand();alter.CommandText=$"ALTER TABLE audit_trail ADD COLUMN {addition.Item1} {addition.Item2}";alter.ExecuteNonQuery();}
    }
    // Gravações do dia a dia: no máximo uma cópia de segurança a cada 10 minutos (pedido do usuário, 20/10/2026; antes era uma por
    // gravação, ~0,9 s cada). Restauração, migração e abertura do banco continuam fazendo a cópia sempre (CreateBackupUnsafe).
    static readonly TimeSpan RoutineBackupInterval=TimeSpan.FromMinutes(10);
    DateTime? lastRoutineBackupUtc;
    void CreateRoutineBackupUnsafe()
    {
        if(lastRoutineBackupUtc==null&&Directory.Exists(BackupFolder))lastRoutineBackupUtc=Directory.GetFiles(BackupFolder,"caixa_*.db").Select(File.GetLastWriteTimeUtc).DefaultIfEmpty(DateTime.MinValue).Max();
        if(lastRoutineBackupUtc is DateTime last&&DateTime.UtcNow-last<RoutineBackupInterval)return;
        CreateBackupUnsafe();lastRoutineBackupUtc=DateTime.UtcNow;
    }    void CreateBackupUnsafe()
    {
        if(!File.Exists(DatabasePath)) return;
        Directory.CreateDirectory(BackupFolder);
        var destination=Path.Combine(BackupFolder,$"caixa_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.db");
        using(var source=Open()) using(var backup=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=destination,Pooling=false}.ToString())) { backup.Open(); source.BackupDatabase(backup); }
        foreach(var old in Directory.GetFiles(BackupFolder,"caixa_*.db").OrderByDescending(File.GetLastWriteTimeUtc).Skip(BusinessRules.Current.InternalBackups)) { try { File.Delete(old); } catch(IOException) { } catch(UnauthorizedAccessException) { } }
    }
    // Fila do próprio processo antes da trava de arquivo: quem espera é chamado assim que a operação anterior termina
    // (antes cada espera dormia 100 ms e tentava de novo). Continua uma operação por vez; o arquivo protege contra outros programas.
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string,SemaphoreSlim> gates=new(StringComparer.OrdinalIgnoreCase);
    T WithLock<T>(Func<T> work)
    {
        var gate=gates.GetOrAdd(Path.GetFullPath(LockFile),_=>new SemaphoreSlim(1,1));
        if(!gate.Wait(TimeSpan.FromSeconds(10)))throw new IOException("Os dados estão ocupados. Tente novamente.");
        try{return WithFileLock(work);}finally{gate.Release();}
    }
    T WithFileLock<T>(Func<T> work)
    {
        Directory.CreateDirectory(Folder); FileStream? lease=null; Exception? last=null;
        for(var i=0;i<100 && lease==null;i++) { try { lease=new FileStream(LockFile,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None); } catch(IOException ex) { last=ex;Thread.Sleep(100); } }
        if(lease==null) throw new IOException("Os dados estão ocupados. Tente novamente.",last);
        using(lease) return work();
    }
    static void Read(SqliteConnection c,string sql,Action<SqliteDataReader> row){using var cmd=c.CreateCommand();cmd.CommandText=sql;using var r=cmd.ExecuteReader();while(r.Read())row(r);}static void Execute(SqliteConnection c,SqliteTransaction tx,string sql,params (string Name,object? Value)[] values){if(prepared!=null){if(prepared.TryGetValue(sql,out var kept)){foreach(var p in values)kept.Parameters[p.Name].Value=Db(p.Value);}else{kept=c.CreateCommand();kept.Transaction=tx;kept.CommandText=sql;foreach(var p in values)kept.Parameters.AddWithValue(p.Name,Db(p.Value));prepared[sql]=kept;}kept.ExecuteNonQuery();return;}using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText=sql;foreach(var p in values)cmd.Parameters.AddWithValue(p.Name,Db(p.Value));cmd.ExecuteNonQuery();}static object Db(object? value)=>value switch{null=>DBNull.Value,Guid g=>g.ToString(),bool b=>b?1:0,_=>value};
    static void DeleteMissing(SqliteConnection c,SqliteTransaction tx,string table,List<string> ids){if(ids.Count==0){Execute(c,tx,$"DELETE FROM {table}");return;}using var cmd=c.CreateCommand();cmd.Transaction=tx;var names=new List<string>();for(var i=0;i<ids.Count;i++){var n=$"@p{i}";names.Add(n);cmd.Parameters.AddWithValue(n,ids[i]);}cmd.CommandText=$"DELETE FROM {table} WHERE id NOT IN ({string.Join(',',names)})";cmd.ExecuteNonQuery();}static void DeleteOrphans(SqliteConnection c,SqliteTransaction tx,string table,string foreign,string parent)=>Execute(c,tx,$"DELETE FROM {table} WHERE {foreign} NOT IN (SELECT id FROM {parent})");
    // Auditoria é só de acréscimo: insere os registros novos e nunca apaga/regrava os antigos (antes regravava tudo a cada salvamento).
    static void InsertNewAudit(SqliteConnection c,SqliteTransaction tx,List<AuditEntry> entries){var existing=new HashSet<string>(StringComparer.OrdinalIgnoreCase);using(var cmd=c.CreateCommand()){cmd.Transaction=tx;cmd.CommandText="SELECT id FROM audit_trail";using var r=cmd.ExecuteReader();while(r.Read())existing.Add(r.GetString(0));}foreach(var x in entries){if(existing.Contains(x.Id.ToString()))continue;Execute(c,tx,"INSERT INTO audit_trail(id,timestamp,action,description,computer,sale_id,user_name,entity_type,entity_id,old_value,new_value,reason,user_id,user_login,session_id) VALUES(@id,@v0,@v1,@v2,@v3,@v4,@v5,@v6,@v7,@v8,@v9,@v10,@v11,@v12,@v13)",("@id",x.Id),("@v0",T(x.Timestamp)),("@v1",x.Action),("@v2",x.Description),("@v3",x.Computer),("@v4",x.SaleId),("@v5",x.UserName),("@v6",x.EntityType),("@v7",x.EntityId),("@v8",x.OldValue),("@v9",x.NewValue),("@v10",x.Reason),("@v11",x.UserId),("@v12",x.UserLogin),("@v13",x.SessionId));}}
    static void ReplaceSimple(SqliteConnection c,SqliteTransaction tx,string table,List<(Guid Id,object?[] Values)> rows,string columns){Execute(c,tx,$"DELETE FROM {table}");var names=columns.Split(',').Select((_,i)=>$"@v{i}").ToArray();foreach(var row in rows){var args=new List<(string,object?)>{("@id",row.Id)};args.AddRange(row.Values.Select((v,i)=>(names[i],v)));Execute(c,tx,$"INSERT INTO {table}(id,{columns}) VALUES(@id,{string.Join(',',names)})",args.ToArray());}}
    static long ScalarLong(SqliteConnection c,string sql){using var cmd=c.CreateCommand();cmd.CommandText=sql;return Convert.ToInt64(cmd.ExecuteScalar());}static string S(SqliteDataReader r,int i)=>r.IsDBNull(i)?"":r.GetString(i);static Guid G(SqliteDataReader r,int i)=>Guid.Parse(r.GetString(i));static Guid? GN(SqliteDataReader r,int i)=>r.IsDBNull(i)?null:Guid.Parse(r.GetString(i));static DateTime D(SqliteDataReader r,int i)=>DateTime.Parse(r.GetString(i),Invariant,DateTimeStyles.RoundtripKind);static DateTime? DN(SqliteDataReader r,int i)=>r.IsDBNull(i)?null:D(r,i);static decimal M(SqliteDataReader r,int i)=>decimal.Parse(r.GetString(i),Invariant);static decimal? MN(SqliteDataReader r,int i)=>r.IsDBNull(i)?null:M(r,i);static bool B(SqliteDataReader r,int i)=>r.GetInt64(i)!=0;static string T(DateTime x)=>x.ToString("O",Invariant);static object TN(DateTime? x)=>x is null?DBNull.Value:T(x.Value);static string N(decimal x)=>x.ToString(Invariant);static object NN(decimal? x)=>x is null?DBNull.Value:N(x.Value);
}

// UX 2.0 (pacote 9): fases do pedido passam de 8 para 5 (Arte → Aguardando aprovação → Produção → Pronto para retirada → Entregue).
// Migração segura: backup verificado antes, conversão só do texto da fase (não mexe em valores, pagamentos, prazos, caixa nem em auditoria/histórico),
// tabela order_stage_migration guarda a fase antiga de cada pedido convertido (reversível com Rollback) e contagem por fase antes/depois é conferida.
// Roda uma única vez (metadata order_stages_v2). Como o modelo converte nomes antigos ao ler, nada quebra se algum computador antigo ainda enviar o nome velho.
// NF registrada em dobro (autorizado pelo usuário em 26/10/2026): a lista de notas ficava vazia sem o PIN do dono e a mesma NF
// foi registrada de novo (pedido #152, NF 469). Uma única vez: tira só as cópias idênticas (mesmo pedido, número, tipo, data,
// valor, observação e arquivos), mantém a primeira e registra na auditoria. Marca 'fiscal_dedupe_v1' no metadata.
public static class FiscalDedupe
{
    const string Key="fiscal_dedupe_v1";
    public static int Apply(SqliteConnection c)
    {
        using var tx=c.BeginTransaction();
        string? Scalar(string sql){using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText=sql;return cmd.ExecuteScalar() as string;}
        void Run(string sql,params (string,object)[] values){using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText=sql;foreach(var (n,v) in values)cmd.Parameters.AddWithValue(n,v);cmd.ExecuteNonQuery();}
        if(Scalar($"SELECT value FROM metadata WHERE key='{Key}'")!=null)return 0;
        var removed=new List<System.Text.Json.Nodes.JsonNode>();
        if(Scalar("SELECT value FROM metadata WHERE key='prompt12_v1'") is string text&&System.Text.Json.Nodes.JsonNode.Parse(text) is System.Text.Json.Nodes.JsonObject state&&state["FiscalDocuments"] is System.Text.Json.Nodes.JsonArray documents)
        {
            static string Same(System.Text.Json.Nodes.JsonNode? x)=>string.Join("|",new[]{"OrderId","Number","Type","Date","Amount","Notes","PdfPath","XmlPath"}.Select(k=>x?[k]?.ToJsonString()??""));
            var seen=new HashSet<string>();
            foreach(var document in documents.ToList())if(!seen.Add(Same(document))){removed.Add(document!.DeepClone());documents.Remove(document);}
            if(removed.Count>0)
            {
                Run("UPDATE metadata SET value=@v WHERE key='prompt12_v1'",("@v",state.ToJsonString()));
                Run("INSERT INTO audit_trail(id,timestamp,action,description,computer,sale_id,user_name,entity_type,entity_id,old_value,new_value,reason,user_id,user_login,session_id) VALUES(@id,@at,'Documento fiscal duplicado removido',@d,'Servidor',NULL,'Sistema','Documento fiscal','',@old,'','Registro repetido da mesma NF (atualização 2026.10.26.1)','','','')",
                    ("@id",Guid.NewGuid().ToString()),("@at",DateTime.Now.ToString("O",CultureInfo.InvariantCulture)),("@d",$"{removed.Count} registro(s) repetido(s): NF "+string.Join(", ",removed.Select(x=>x["Number"]?.GetValue<string>()))),("@old",new System.Text.Json.Nodes.JsonArray(removed.ToArray()).ToJsonString()));
                Run("INSERT INTO metadata(key,value) VALUES('data_version','1') ON CONFLICT(key) DO UPDATE SET value=CAST(CAST(value AS INTEGER)+1 AS TEXT)");
            }
        }
        Run($"INSERT INTO metadata(key,value) VALUES('{Key}','done') ON CONFLICT(key) DO UPDATE SET value='done'");
        tx.Commit();return removed.Count;
    }
}
public static class StageMigration
{
    const string Key="order_stages_v2";
    public sealed record Report(IReadOnlyDictionary<string,int> Before,IReadOnlyDictionary<string,int> After,int Converted,string? BackupPath);
    public static Dictionary<string,int> Count(SqliteConnection c)
    {
        var result=new Dictionary<string,int>();using var cmd=c.CreateCommand();cmd.CommandText="SELECT stage,COUNT(*) FROM scheduled_orders GROUP BY stage";using var reader=cmd.ExecuteReader();
        while(reader.Read())result[reader.GetString(0)]=reader.GetInt32(1);return result;
    }
    // "done" = já convertido; "rolled-back" = alguém desfez de propósito: não converter de novo sozinho (só com Reapply).
    static bool Done(SqliteConnection c){using var cmd=c.CreateCommand();cmd.CommandText="SELECT value FROM metadata WHERE key=@k";cmd.Parameters.AddWithValue("@k",Key);return cmd.ExecuteScalar() is string v&&v is "done" or "rolled-back";}
    public static Report? Reapply(SqliteConnection c,string backupFolder){Run(c,null,"DELETE FROM metadata WHERE key=@k",("@k",Key));return Apply(c,backupFolder);}
    static void Run(SqliteConnection c,SqliteTransaction? tx,string sql,params (string,object)[] p){using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText=sql;foreach(var (n,v) in p)cmd.Parameters.AddWithValue(n,v);cmd.ExecuteNonQuery();}
    // Normaliza a contagem: o que está no banco hoje, já convertido para os nomes novos (usado para conferir antes/depois).
    static Dictionary<string,int> Normalized(Dictionary<string,int> counts){var r=new Dictionary<string,int>();foreach(var (stage,n) in counts){var key=OrderStages.Normalize(stage);r[key]=r.GetValueOrDefault(key)+n;}return r;}
    public static Report? Apply(SqliteConnection c,string backupFolder)
    {
        if(Done(c))return null;
        var before=Count(c);
        if(!before.Keys.Any(OrderStages.Legacy.ContainsKey)){Run(c,null,"INSERT INTO metadata(key,value) VALUES(@k,'done') ON CONFLICT(key) DO UPDATE SET value='done'",("@k",Key));return null;}
        Directory.CreateDirectory(backupFolder);
        var backup=Path.Combine(backupFolder,"antes-fases-do-pedido-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..8]+".db");
        using(var destination=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=backup,Pooling=false}.ToString())){destination.Open();c.BackupDatabase(destination);}
        // Backup só vale se abrir, passar no teste de integridade e tiver o mesmo número de pedidos.
        using(var check=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=backup,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString()))
        {
            check.Open();using(var cmd=check.CreateCommand()){cmd.CommandText="PRAGMA integrity_check";if(!"ok".Equals(cmd.ExecuteScalar()?.ToString(),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Backup antes da migração das fases não passou na verificação. Nada foi alterado.");}
            if(Count(check).Values.Sum()!=before.Values.Sum())throw new InvalidOperationException("Backup antes da migração das fases está incompleto. Nada foi alterado.");
        }
        var converted=0;
        using(var tx=c.BeginTransaction())
        {
            Run(c,tx,"CREATE TABLE IF NOT EXISTS order_stage_migration(order_id TEXT PRIMARY KEY,order_number INTEGER NOT NULL,old_stage TEXT NOT NULL,new_stage TEXT NOT NULL,migrated_at TEXT NOT NULL)");
            foreach(var (old,current) in OrderStages.Legacy)
            {
                Run(c,tx,"INSERT OR IGNORE INTO order_stage_migration(order_id,order_number,old_stage,new_stage,migrated_at) SELECT id,order_number,stage,@new,@at FROM scheduled_orders WHERE stage=@old",("@new",current),("@old",old),("@at",DateTimeOffset.Now.ToString("O")));
                using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="UPDATE scheduled_orders SET stage=@new,revision=revision+1 WHERE stage=@old";cmd.Parameters.AddWithValue("@new",current);cmd.Parameters.AddWithValue("@old",old);converted+=cmd.ExecuteNonQuery();
            }
            var after=Count(c);var expected=Normalized(before);
            if(after.Values.Sum()!=before.Values.Sum()||after.Keys.Any(OrderStages.Legacy.ContainsKey)||expected.Count!=after.Count||expected.Any(x=>after.GetValueOrDefault(x.Key)!=x.Value))
                throw new InvalidOperationException("A contagem de pedidos por fase não bateu depois da conversão. A migração foi desfeita (backup: "+backup+").");
            Run(c,tx,"INSERT INTO metadata(key,value) VALUES('data_version','1') ON CONFLICT(key) DO UPDATE SET value=CAST(CAST(value AS INTEGER)+1 AS TEXT)");
            Run(c,tx,"INSERT INTO metadata(key,value) VALUES(@k,'done') ON CONFLICT(key) DO UPDATE SET value='done'",("@k",Key));
            tx.Commit();
            return new Report(before,after,converted,backup);
        }
    }
    // Desfaz: devolve cada pedido convertido à fase que tinha (só se ainda estiver na fase para a qual foi convertido).
    public static int Rollback(SqliteConnection c)
    {
        using var tx=c.BeginTransaction();int restored;
        using(var cmd=c.CreateCommand()){cmd.Transaction=tx;cmd.CommandText="UPDATE scheduled_orders SET stage=(SELECT old_stage FROM order_stage_migration m WHERE m.order_id=scheduled_orders.id),revision=revision+1 WHERE id IN (SELECT order_id FROM order_stage_migration m2 WHERE m2.new_stage=scheduled_orders.stage)";restored=cmd.ExecuteNonQuery();}
        Run(c,tx,"INSERT INTO metadata(key,value) VALUES(@k,'rolled-back') ON CONFLICT(key) DO UPDATE SET value='rolled-back'",("@k",Key));
        Run(c,tx,"INSERT INTO metadata(key,value) VALUES('data_version','1') ON CONFLICT(key) DO UPDATE SET value=CAST(CAST(value AS INTEGER)+1 AS TEXT)");
        tx.Commit();return restored;
    }
}
public sealed partial class DataStore
{
    // Regras de negócio (business_rules_v1): inválidas ou ausentes voltam ao padrão, sem impedir o sistema de abrir.
    static BusinessRules ParseRules(string text)
    {
        try { var rules = JsonSerializer.Deserialize<BusinessRules>(text) ?? new(); rules.Validate(); return rules; }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return new(); }
    }
    static void LoadRulesUnsafe(SqliteConnection c)
    {
        using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT value FROM metadata WHERE key='business_rules_v1'";
        BusinessRules.Current = cmd.ExecuteScalar() is string text ? ParseRules(text) : new();
    }
}

// Auditoria enxuta (autorizado pelo usuário em 20/10/2026): eventos "Cadastro / configuração alterado" antigos guardavam a lista inteira
// (ex.: todas as dívidas avulsas) antes e depois, ~300 KB cada. Uma única vez: cópia completa verificada do banco
// (Backups/auditoria-original-*.db, fora do rodízio), depois cada evento passa a guardar só os itens que entraram, saíram ou mudaram
// — nenhuma mudança registrada se perde — e o arquivo é compactado (VACUUM). Marca 'audit_compact_v1' no metadata.
public static class AuditCompaction
{
    const string Key="audit_compact_v1";
    public static (List<JsonElement> Before,List<JsonElement> After)? ChangedItems(JsonElement b,JsonElement a)
    {
        if(b.ValueKind!=JsonValueKind.Array||a.ValueKind!=JsonValueKind.Array)return null;
        static string? Id(JsonElement e)=>e.ValueKind==JsonValueKind.Object&&e.TryGetProperty("Id",out var id)?id.ToString():null;
        if(b.EnumerateArray().Any(x=>Id(x)==null)||a.EnumerateArray().Any(x=>Id(x)==null))return null;
        var bm=b.EnumerateArray().GroupBy(x=>Id(x)!).ToDictionary(g=>g.Key,g=>g.First().GetRawText());var am=a.EnumerateArray().GroupBy(x=>Id(x)!).ToDictionary(g=>g.Key,g=>g.First().GetRawText());
        var removed=b.EnumerateArray().Where(x=>!am.TryGetValue(Id(x)!,out var y)||y!=x.GetRawText()).Select(x=>x.Clone()).ToList();
        var added=a.EnumerateArray().Where(x=>!bm.TryGetValue(Id(x)!,out var y)||y!=x.GetRawText()).Select(x=>x.Clone()).ToList();
        return (removed,added);
    }
    static long Scalar(SqliteConnection c,string sql){using var cmd=c.CreateCommand();cmd.CommandText=sql;return Convert.ToInt64(cmd.ExecuteScalar()??0);}
    public static string? Apply(SqliteConnection c,string backupFolder)
    {
        using(var flag=c.CreateCommand()){flag.CommandText="SELECT COUNT(*) FROM metadata WHERE key=@k";flag.Parameters.AddWithValue("@k",Key);if(Convert.ToInt64(flag.ExecuteScalar())>0)return null;}
        const string Where="action='Cadastro / configuração alterado' AND LENGTH(old_value)+LENGTH(new_value)>20000";
        var targets=Scalar(c,$"SELECT COUNT(*) FROM audit_trail WHERE {Where}");
        string? backup=null;long before=0,after=0;var compacted=0;
        if(targets>0)
        {
            Directory.CreateDirectory(backupFolder);
            backup=Path.Combine(backupFolder,"auditoria-original-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..8]+".db");
            using(var destination=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=backup,Pooling=false}.ToString())){destination.Open();c.BackupDatabase(destination);}
            // A cópia só vale se abrir, passar na integridade e tiver a mesma auditoria (quantidade e tamanho).
            var rows=Scalar(c,"SELECT COUNT(*) FROM audit_trail");before=Scalar(c,"SELECT COALESCE(SUM(LENGTH(old_value)+LENGTH(new_value)),0) FROM audit_trail");
            using(var check=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=backup,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString()))
            {
                check.Open();using(var integrity=check.CreateCommand()){integrity.CommandText="PRAGMA integrity_check";if(integrity.ExecuteScalar()?.ToString()!="ok")throw new InvalidOperationException("Cópia antes de compactar a auditoria falhou na verificação; nada foi alterado.");}
                if(Scalar(check,"SELECT COUNT(*) FROM audit_trail")!=rows||Scalar(check,"SELECT COALESCE(SUM(LENGTH(old_value)+LENGTH(new_value)),0) FROM audit_trail")!=before)throw new InvalidOperationException("Cópia antes de compactar a auditoria não confere; nada foi alterado.");
            }
            SqliteConnection.ClearPool(new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=backup,Pooling=false}.ToString()));
            var updates=new List<(string Id,string Old,string New)>();
            using(var read=c.CreateCommand()){read.CommandText=$"SELECT id,old_value,new_value FROM audit_trail WHERE {Where}";using var r=read.ExecuteReader();
                while(r.Read()){try{using var o=JsonDocument.Parse(r.GetString(1));using var n=JsonDocument.Parse(r.GetString(2));if(ChangedItems(o.RootElement,n.RootElement) is var (b,a))updates.Add((r.GetString(0),JsonSerializer.Serialize(b),JsonSerializer.Serialize(a)));}catch(JsonException){}}}
            using(var tx=c.BeginTransaction())
            {
                foreach(var u in updates){using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="UPDATE audit_trail SET old_value=@o,new_value=@n WHERE id=@id";cmd.Parameters.AddWithValue("@o",u.Old);cmd.Parameters.AddWithValue("@n",u.New);cmd.Parameters.AddWithValue("@id",u.Id);cmd.ExecuteNonQuery();compacted++;}
                using(var mark=c.CreateCommand()){mark.Transaction=tx;mark.CommandText="INSERT INTO metadata(key,value) VALUES(@k,@v)";mark.Parameters.AddWithValue("@k",Key);mark.Parameters.AddWithValue("@v",JsonSerializer.Serialize(new{at=DateTime.Now,compacted,backup}));mark.ExecuteNonQuery();}
                tx.Commit();
            }
            after=Scalar(c,"SELECT COALESCE(SUM(LENGTH(old_value)+LENGTH(new_value)),0) FROM audit_trail");
            using(var vacuum=c.CreateCommand()){vacuum.CommandText="VACUUM";vacuum.ExecuteNonQuery();}
        }
        else using(var mark=c.CreateCommand()){mark.CommandText="INSERT INTO metadata(key,value) VALUES(@k,@v)";mark.Parameters.AddWithValue("@k",Key);mark.Parameters.AddWithValue("@v","{\"compacted\":0}");mark.ExecuteNonQuery();}
        return $"Auditoria compactada: {compacted} evento(s); {before/1048576.0:N1} MB → {after/1048576.0:N1} MB; cópia: {backup}";
    }
}
