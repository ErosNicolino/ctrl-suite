using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
namespace GraficaCaixa;

// Leitura/gravação direta de uma chave de configuração (sem regravar o banco inteiro) e cópia de backup para outra pasta.
public sealed partial class DataStore
{
    public string? ReadMetadataValue(string key)=>WithLock(()=>{using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT value FROM metadata WHERE key=@k";cmd.Parameters.AddWithValue("@k",key);return cmd.ExecuteScalar() as string;});
    public void WriteMetadataValue(string key,string value)=>WithLock(()=>{using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="INSERT INTO metadata(key,value) VALUES(@k,@v) ON CONFLICT(key) DO UPDATE SET value=excluded.value";cmd.Parameters.AddWithValue("@k",key);cmd.Parameters.AddWithValue("@v",value);cmd.ExecuteNonQuery();return 0;});

    // Copia o banco ativo (backup online do SQLite), confere a integridade da cópia e mantém só as `keep` mais recentes no destino.
    public string BackupTo(string destination,int keep)
    {
        Directory.CreateDirectory(destination);
        var file=Path.Combine(destination,$"{BackupScheduler.Prefix}{DateTime.Now:yyyy-MM-dd_HH'h'mm'm'ss}.db");
        var temp=file+".parcial";
        // A cópia sai em modo DELETE (arquivo único): em WAL, a conferência só leitura deixava ".parcial-shm/-wal" para trás em cada backup.
        WithLock(()=>{using var source=Open();using var target=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=temp,Pooling=false}.ToString());target.Open();source.BackupDatabase(target);using var mode=target.CreateCommand();mode.CommandText="PRAGMA journal_mode=DELETE";mode.ExecuteNonQuery();return 0;});
        using(var check=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=temp,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString())){check.Open();using var cmd=check.CreateCommand();cmd.CommandText="PRAGMA integrity_check";if(cmd.ExecuteScalar() as string!="ok")throw new InvalidDataException("A cópia não passou na verificação de integridade.");}
        SqliteConnection.ClearAllPools();File.Move(temp,file,true);
        foreach(var old in Directory.GetFiles(destination,BackupScheduler.Prefix+"*.db").OrderByDescending(File.GetLastWriteTimeUtc).Skip(Math.Max(1,keep))){try{File.Delete(old);}catch(IOException){}catch(UnauthorizedAccessException){}}
        // Sobras de cópias anteriores (".parcial", ".parcial-shm", ".parcial-wal"): só as com mais de 1 hora, para nunca tocar numa cópia em andamento.
        foreach(var leftover in Directory.GetFiles(destination,BackupScheduler.Prefix+"*.db.parcial*").Where(f=>File.GetLastWriteTimeUtc(f)<DateTime.UtcNow.AddHours(-1))){try{File.Delete(leftover);}catch(IOException){}catch(UnauthorizedAccessException){}}
        return file;
    }
}

// Backup programado no serviço do computador principal: confere a cada minuto se está na hora, faz a cópia e registra o resultado.
// Se o PC estava desligado no horário, faz o backup atrasado assim que o serviço voltar.
public sealed class BackupScheduler:IDisposable
{
    public const string Prefix="erp_backup_",StatusKey="backup_status_v1",ScheduleKey="backup_schedule_v1";
    static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    readonly DataStore store;readonly Action<string,string>? log;readonly Timer timer;readonly IReadOnlyList<(string Name,string Folder)> files;int running;
    public BackupScheduler(DataStore store,Action<string,string>? log=null,IReadOnlyList<(string Name,string Folder)>? files=null){this.store=store;this.log=log;this.files=files??[];timer=new Timer(_=>Tick(),null,TimeSpan.FromSeconds(30),TimeSpan.FromMinutes(1));}

    public BackupSchedule Schedule(){try{var raw=store.ReadMetadataValue(ScheduleKey);return raw==null?new():JsonSerializer.Deserialize<BackupSchedule>(raw,Json)??new();}catch(JsonException){return new();}}
    public BackupStatus Status(){BackupStatus? saved=null;try{var raw=store.ReadMetadataValue(StatusKey);if(raw!=null)saved=JsonSerializer.Deserialize<BackupStatus>(raw,Json);}catch(JsonException){}var schedule=Schedule();var last=saved?.LastAttemptAt;return (saved??new BackupStatus(null,null,"","",null)) with {NextRunAt=schedule.Enabled?NextRun(schedule,last,DateTime.Now):null};}

    // Próxima execução depois da última tentativa. Sem tentativa anterior: intervalo = agora; diário/semanal = próximo horário válido (inclusive hoje se já passou).
    public static DateTime? NextRun(BackupSchedule s,DateTime? last,DateTime now)
    {
        if(!s.Enabled)return null;
        if(s.Mode=="intervalo"){var hours=Math.Clamp(s.IntervalHours,1,168);return last is DateTime l?l.AddHours(hours):now;}
        if(!TimeSpan.TryParseExact(s.Time,@"hh\:mm",CultureInfo.InvariantCulture,out var at))at=new TimeSpan(19,0,0);
        var days=s.Mode=="semanal"&&s.Days.Count>0?s.Days.ToHashSet():Enumerable.Range(0,7).ToHashSet();
        var from=(last??now).Date;
        for(var d=0;d<=8;d++){var candidate=from.AddDays(d).Add(at);if((last==null?candidate>=now:candidate>last)&&days.Contains((int)candidate.DayOfWeek))return candidate;}
        return null;
    }

    void Tick()
    {
        try{var schedule=Schedule();if(!schedule.Enabled||string.IsNullOrWhiteSpace(schedule.Destination))return;var status=Status();if(status.NextRunAt is DateTime next&&next<=DateTime.Now)Run(schedule);}
        catch(Exception ex){log?.Invoke("backup-agendado-erro",ex.Message);}
    }
    // Executa agora (agendado ou pelo botão "Fazer backup agora"). Uma execução por vez.
    public BackupStatus Run(BackupSchedule? schedule=null)
    {
        schedule??=Schedule();
        if(string.IsNullOrWhiteSpace(schedule.Destination))throw new InvalidOperationException("Escolha a pasta de destino do backup.");
        if(Interlocked.Exchange(ref running,1)==1)throw new InvalidOperationException("Um backup já está em andamento. Aguarde alguns segundos.");
        var previous=Status();var now=DateTime.Now;BackupStatus result;
        try{var file=store.BackupTo(schedule.Destination,Math.Clamp(schedule.KeepCopies,1,365));
            // O banco não guarda fotos nem PDFs: vão junto, em Arquivos no mesmo destino. Falha aqui não invalida a cópia do banco.
            var mirror=MirrorFiles(files,schedule.Destination);if(mirror.Error.Length>0)log?.Invoke("backup-arquivos-falhou",mirror.Error);
            result=new BackupStatus(now,DateTime.Now,file,"",null,mirror.Copied,mirror.Total,mirror.Error);log?.Invoke("backup-agendado",file);}
        catch(Exception ex){result=new BackupStatus(now,previous.LastSuccessAt,previous.LastFile,ex.Message,null);log?.Invoke("backup-agendado-falhou",ex.Message);}
        finally{Interlocked.Exchange(ref running,0);}
        store.WriteMetadataValue(StatusKey,JsonSerializer.Serialize(result,Json));
        return result with {NextRunAt=NextRun(schedule,now,DateTime.Now)};
    }
    // Pastas de arquivos do ERP que o banco não guarda; os documentos do celular, quando ficam em outra pasta, vão em "Celular".
    public static List<(string Name,string Folder)> FilesOf(string? erpRoot,string? documents)
    {
        var folders=new List<(string Name,string Folder)>();
        if(!string.IsNullOrWhiteSpace(erpRoot))foreach(var name in new[]{"Produtos","Recibos","Fechamentos","Orcamentos","Impressões"})folders.Add((name,Path.Combine(erpRoot,name)));
        if(!string.IsNullOrWhiteSpace(documents)&&!string.Equals(Path.GetFullPath(documents),string.IsNullOrWhiteSpace(erpRoot)?"":Path.GetFullPath(erpRoot),StringComparison.OrdinalIgnoreCase))folders.Add(("Celular",documents));
        return folders;
    }
    // Espelha as pastas em <destino>\Arquivos\<nome>: copia só o que é novo ou mudou (tamanho ou data) e nunca apaga nada no destino,
    // para uma foto removida por engano continuar no backup. Grava em .parcial e só então troca, sem deixar arquivo pela metade.
    public static (int Copied,int Total,string Error) MirrorFiles(IEnumerable<(string Name,string Folder)> folders,string destination)
    {
        int copied=0,total=0,failed=0;var error="";
        foreach(var (name,folder) in folders)
        {
            if(!Directory.Exists(folder))continue;
            try
            {
                foreach(var source in Directory.EnumerateFiles(folder,"*",SearchOption.AllDirectories))
                {
                    total++;
                    try
                    {
                        var target=Path.Combine(destination,"Arquivos",name,Path.GetRelativePath(folder,source));var from=new FileInfo(source);var to=new FileInfo(target);
                        if(to.Exists&&to.Length==from.Length&&to.LastWriteTimeUtc==from.LastWriteTimeUtc)continue;
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);var temp=target+".parcial";
                        File.Copy(source,temp,true);File.SetLastWriteTimeUtc(temp,from.LastWriteTimeUtc);File.Move(temp,target,true);copied++;
                    }
                    catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){failed++;error=$"{failed} arquivo(s) não copiado(s). Último: {Path.GetFileName(source)} ({ex.Message})";}
                }
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){failed++;error=$"Pasta {name}: {ex.Message}";}
        }
        return (copied,total,error);
    }
    // Validação do destino (feita no PC principal, que é quem grava): pasta completa e com permissão de escrita.
    public static void ValidateDestination(string destination)
    {
        if(!Path.IsPathFullyQualified(destination))throw new InvalidOperationException("Informe a pasta completa do backup, como E:\\Backup-ERP.");
        try{Directory.CreateDirectory(destination);var probe=Path.Combine(destination,".teste-erp-"+Guid.NewGuid().ToString("N"));File.WriteAllText(probe,"ok");File.Delete(probe);}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){throw new InvalidOperationException("O computador principal não conseguiu gravar em "+destination+": "+ex.Message);}
    }
    public void Dispose()=>timer.Dispose();
}
