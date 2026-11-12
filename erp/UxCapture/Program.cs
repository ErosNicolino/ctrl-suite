using System.Runtime.CompilerServices;
using GraficaCaixa;
using GraficaCaixa.Wpf;
using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

// Uso: UxCapture --saida <pasta> --banco <caixa_dados.db> [--largura 1366 --altura 768] [--combos claro-1,escuro-1,claro-1.12,escuro-1.12] [--so <prefixo>]
// O banco informado e copiado com a API de backup do SQLite para uma pasta temporaria; o original so e lido.
// Um usuario sintetico (senha aleatoria, nunca exibida) e criado SOMENTE na copia, para abrir as telas.
static class Program
{
    static string output = "", temp = "", only = "", pdfDir = ""; static string[] args0 = [];
    static int width = 1366, height = 768;
    static bool measureOnly;
    static readonly List<string> log = [];
    static readonly List<string> times = ["combo;tela;ms"];

    [STAThread]
    static int Main(string[] args)
    {
        string Arg(string name, string fallback) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback; }
        output = Path.GetFullPath(Arg("--saida", "capturas"));
        var database = Path.GetFullPath(Arg("--banco", ""));
        width = int.Parse(Arg("--largura", "1366")); height = int.Parse(Arg("--altura", "768"));
        only = Arg("--so", ""); pdfDir = Arg("--pdfs", ""); args0 = args; measureOnly = args.Contains("--tempos");
        var combos = Arg("--combos", "claro-1,escuro-1,claro-1.12,escuro-1.12").Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (!File.Exists(database)) { Console.WriteLine("Banco nao encontrado: " + database); return 2; }
        Directory.CreateDirectory(output);
        temp = Path.Combine(Path.GetTempPath(), "erp_captura_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(temp, "Dados"));
        Environment.SetEnvironmentVariable("GRAFICA_CAIXA_PERFIL", Path.Combine(temp, "Perfil"));
        var app = new App(); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown; ThemeManager.Initialize();
        if (args.Contains("--diag"))
        {
            var w = (Window)Activator.CreateInstance(typeof(App).Assembly.GetTypes().First(t => t.Name == "SampleDialog"), true)!;
            w.Left = -30000; w.ShowActivated = false; w.Show(); w.UpdateLayout();
            var df = (DialogFrame)w.Content;
            Console.WriteLine($"Accent no frame: {Ui.GetAccent(df)} local={df.ReadLocalValue(Ui.AccentProperty)}");
            foreach (var b in Visuals(df).OfType<System.Windows.Controls.Border>().Take(12)) Console.WriteLine($"Border bg={b.Background} h={b.ActualHeight:0} w={b.ActualWidth:0} expr={System.Windows.Data.BindingOperations.GetBindingExpression(b, System.Windows.Controls.Border.BackgroundProperty)?.Status}");
            w.Close(); app.Shutdown(); return 0;
        }
        var frame = new DispatcherFrame(); int code = 0;
        app.Dispatcher.BeginInvoke(async () =>
        {
            try { await Run(database, combos); }
            catch (Exception ex) { Console.WriteLine(ex); Log("ERRO GERAL: " + ex); code = 1; }
            finally { frame.Continue = false; }
        });
        Dispatcher.PushFrame(frame);
        File.WriteAllLines(Path.Combine(output, "capturas.txt"), log, Encoding.UTF8);
        File.WriteAllLines(Path.Combine(output, "tempos.csv"), times, Encoding.UTF8);
        app.Shutdown();
        try { SqliteConnection.ClearAllPools(); Directory.Delete(temp, true); } catch { }
        return code;
    }

    static async Task Run(string database, string[] combos)
    {
        var dataFolder = Path.Combine(temp, "Dados");
        using (var source = new SqliteConnection($"Data Source={database};Mode=ReadOnly;Pooling=False"))
        using (var target = new SqliteConnection($"Data Source={Path.Combine(dataFolder, "caixa_dados.db")};Pooling=False"))
        { source.Open(); target.Open(); source.BackupDatabase(target); }
        Log($"Copia do banco (backup SQLite) em pasta temporaria; original: {database}");

        if (args0.Contains("--ensaio-fases")) { RehearseStages(Path.Combine(dataFolder, "caixa_dados.db"), Path.Combine(dataFolder, "Backups")); return; }
        if (args0.Contains("--compactar")) { var sw0 = System.Diagnostics.Stopwatch.StartNew(); new DataStore(dataFolder).EnsureInitialized(); Log($"Inicialização com compactação: {sw0.Elapsed.TotalSeconds:N1} s | arquivo {new FileInfo(Path.Combine(dataFolder, "caixa_dados.db")).Length / 1048576.0:N1} MB"); }
        if (args0.Contains("--diagnostico-auditoria")) { using var dc = new SqliteConnection($"Data Source={Path.Combine(dataFolder, "caixa_dados.db")};Mode=ReadOnly;Pooling=False"); dc.Open(); using var q = dc.CreateCommand(); q.CommandText = "SELECT action, COUNT(*), SUM(LENGTH(old_value)+LENGTH(new_value))/1048576.0, MIN(timestamp) FROM audit_trail GROUP BY action ORDER BY 3 DESC LIMIT 12"; using (var r = q.ExecuteReader()) while (r.Read()) Log($"AUD {r.GetDouble(2):N1} MB | {r.GetInt64(1)} eventos | desde {r.GetString(3)[..10]} | {r.GetString(0)}"); q.CommandText = "SELECT substr(timestamp,1,7), COUNT(*), SUM(LENGTH(old_value)+LENGTH(new_value))/1048576.0 FROM audit_trail GROUP BY 1 ORDER BY 1"; using (var r = q.ExecuteReader()) while (r.Read()) Log($"MES {r.GetString(0)} | {r.GetInt64(1)} eventos | {r.GetDouble(2):N1} MB"); return; }
        if (args0.Contains("--diagnostico-tamanho")) { var dd = new DataStore(dataFolder).Load(); foreach (var p in typeof(AppData).GetProperties().Where(p => p.CanRead && p.GetIndexParameters().Length == 0)) { var v = p.GetValue(dd); if (v == null) continue; var json = System.Text.Json.JsonSerializer.Serialize(v); if (json.Length > 20000) Log($"TAM {p.Name}: {json.Length / 1024} KB" + (v is System.Collections.ICollection col ? $" ({col.Count} itens)" : "")); } return; }
        if (args0.Contains("--diagnostico-top")) { var dd = new DataStore(dataFolder).Load(); var since = DateTime.Today.AddDays(-90); var items = dd.ManualDebts.Where(x => x.CreatedAt >= since).SelectMany(x => x.Items).Concat(dd.ScheduledOrders.Where(x => x.CreatedAt >= since).SelectMany(x => x.Items)).Where(x => x.Product.Trim().Length > 0).GroupBy(x => x.Product.Trim().ToLowerInvariant()).Select(g => (Name: g.First().Product.Trim(), Count: g.Count(), Qty: g.Sum(i => i.Quantity), Price: g.Last().UnitPrice)).OrderByDescending(x => x.Count).Take(10); foreach (var i in items) Log($"TOP {i.Count}x | qtd {i.Qty} | {i.Price:N2} | {i.Name}"); return; }
        if (args0.Contains("--diagnostico-faturamento")) { var dd = new DataStore(dataFolder).Load(); var from = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1); var ov = new FinancialOverview(dd, from, from.AddMonths(1)); var itemDebts = dd.ManualDebts.Where(x => x.Items.Count > 0 && x.CreatedAt >= from).ToList(); var plainDebts = dd.ManualDebts.Where(x => x.Items.Count == 0 && x.CreatedAt >= from).ToList(); var orders = dd.ScheduledOrders.Where(x => !x.PricePending && x.CreatedAt >= from).ToList(); Log($"Mês {from:MM/yyyy}: Billed={ov.Billed:N2} | pedidos={orders.Count} {orders.Sum(x => x.OrderTotal):N2} | lançamentos com itens (ManualDebt)={itemDebts.Count} {itemDebts.Sum(x => x.Amount):N2} | dívidas sem itens={plainDebts.Count} {plainDebts.Sum(x => x.Amount):N2} | Receipts={ov.Receipts:N2}"); return; }
        var local = new DataStore(dataFolder); local.EnsureInitialized(); localStore = local;
        var docs = Path.Combine(temp, "Documentos"); Directory.CreateDirectory(Path.Combine(docs, "Impressões"));
        string? logoFile = null;
        local.Update(d =>
        {
            var logo = DocumentPaths.ResolveStoredPath(d.DocumentsRoot, d.LogoPath);
            if (!string.IsNullOrWhiteSpace(logo) && File.Exists(logo)) { logoFile = Path.Combine(docs, Path.GetFileName(logo)); File.Copy(logo, logoFile, true); }
            // Fotos dos produtos (cópia, só leitura do original): cards e editor mostram as fotos reais.
            // Pasta do ERP: a configurada no banco ou, se não for absoluta, a pasta acima de Dados\ do banco original.
            var erpRoot = Path.IsPathRooted(d.DocumentsRoot ?? "") ? d.DocumentsRoot! : Path.GetDirectoryName(Path.GetDirectoryName(database))!;
            // As fotos enviadas pelo servidor ficam na pasta de arquivos da web (servidor.json › WebApi.DocumentsDirectory).
            var folders = new List<string> { Path.Combine(erpRoot, "Produtos", "Imagens") };
            var config = Path.Combine(erpRoot, "Configuracao", "servidor.json");
            try { if (File.Exists(config) && JsonNode.Parse(File.ReadAllText(config))?["WebApi"]?["DocumentsDirectory"]?.GetValue<string>() is { Length: > 0 } web) folders.Add(Path.Combine(web, "Produtos", "Imagens")); } catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidOperationException) { }
            var target = Path.Combine(docs, "Produtos", "Imagens"); Directory.CreateDirectory(target);
            foreach (var folder in folders.Where(Directory.Exists)) foreach (var file in Directory.GetFiles(folder, "*.jpg")) File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
            d.DocumentsRoot = docs; d.LogoPath = logoFile == null ? "" : Path.GetFileName(logoFile); return 0;
        });

        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        var credential = LoginSecurity.Create(password);
        using (var c = new SqliteConnection($"Data Source={Path.Combine(dataFolder, "caixa_dados.db")};Pooling=False"))
        {
            c.Open(); using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT INTO auth_users(id,name,login,normalized_login,salt,password_hash,iterations,active,role,created_at,updated_at,must_change) VALUES(@id,'Captura de telas','captura_ux','CAPTURA_UX',@salt,@hash,@it,1,'Administrador',@now,@now,0)";
            cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString()); cmd.Parameters.AddWithValue("@salt", credential.Salt); cmd.Parameters.AddWithValue("@hash", credential.Hash);
            cmd.Parameters.AddWithValue("@it", credential.Iterations); cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }

        var port = FreePort();
        await using var server = await SharedDataServer.StartAsync(local, Path.Combine(temp, "Identidade"), port, logsFolder: Path.Combine(output, "logs-servidor"), webOptions: new WebApiOptions { DocumentsDirectory = docs });
        var conn = server.Connection with { Url = $"https://127.0.0.1:{port}", Urls = null, SelectedUrl = null };
        var store = DataStore.Connect(conn);
        await Task.Run(store.EnsureInitialized);
        var check = await Task.Run(() => store.VerifyLogin("captura_ux", password));
        if (!check.Ok) throw new InvalidOperationException("Login sintetico recusado: " + check.Message);
        var identity = store.AuthenticatedUser!;
        App.CurrentUser = new OperationContext(identity.Name, identity.Role) { Permissions = await store.QueryRemoteAsync<Dictionary<string, bool>>("auth/permissions") };
        if (args0.Contains("--fornecedores")) await SeedSuppliers(local, store);
        if (args0.Contains("--medir-pedido")) { await MeasureOrderSave(local, store); return; }
        if (args0.Contains("--medir-gravacoes")) { await MeasureWrites(local, store); return; }
        App.AuthenticatedStore = store;
        DocumentPaths.BeginSession(conn.Url);
        ReportWriter.OutputFolderResolver = () => Path.Combine(temp, "Fechamentos");
        ReportWriter.OutputFolder = Path.Combine(temp, "Relatorios");
        var bootstrap = await store.QueryRemoteAsync<ModulePage>("api/v1/bootstrap");
        DocumentPaths.SetBootstrap(bootstrap.Data.Deserialize<AppData>()!);

        if (pdfDir.Length > 0) { GeneratePdfs(local, pdfDir); return; }
        if (args0.Contains("--fases")) { CountStages(local); return; }
        if (args0.Contains("--perfil-devedores")) { ProfileDebtors(local); return; }
        if (args0.Contains("--perfil-celular")) { ProfileMobile(local); return; }
        if (args0.Contains("--perfil-relatorios")) { await ProfileReports(store); return; }
        if (args0.Contains("--medir")) { compressedWire = args0.Contains("--comprimido"); await Measure(store, local); return; }
        // Alertas modais inesperados (ex.: erro ao abrir uma tela) são registrados e fechados para a captura não travar.
        var guard = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        guard.Tick += (_, _) => { foreach (var u in Application.Current.Windows.OfType<UnsavedChangesWindow>().ToList()) { Log("ALTERACOES-NAO-SALVAS (falso positivo?): " + (u.Owner?.Title ?? "?")); try { u.DialogResult = false; } catch { u.Close(); } } foreach (var a in Application.Current.Windows.OfType<AlertWindow>().ToList()) { Log("ALERTA: " + ((a.FindName("TitleText") as TextBlock)?.Text) + " — " + ((a.FindName("MessageText") as TextBlock)?.Text)); try { a.DialogResult = false; } catch { a.Close(); } } };
        guard.Start();
        foreach (var combo in combos)
        {
            var parts = combo.Split('-');
            var theme = parts[0] == "escuro" ? AppTheme.Escuro : AppTheme.Claro;
            var scale = double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
            ThemeManager.Set(theme, scale, parts.Length > 2 && parts[2] == "sobria" ? AppPalette.Sobria : AppPalette.Viva, ThemeManager.MotionOff, ThemeManager.DailyPhrase);
            var suffix = $"{parts[0]}-{(scale == 1 ? "normal" : scale < 1.2 ? "grande" : "muitogrande")}{(parts.Length > 2 ? "-" + parts[2] : "")}";
            await CaptureAll(store, suffix);
        }
    }

    // Modo --ensaio-fases: ensaia a migração das fases na CÓPIA do banco (nunca no oficial): antes, converte, depois, desfaz, converte de novo.
    static void RehearseStages(string dbPath, string backups)
    {
        using var c = new SqliteConnection($"Data Source={dbPath};Pooling=False"); c.Open();
        void Show(string title, IReadOnlyDictionary<string, int> d) { Console.WriteLine("-- " + title + " --"); foreach (var (k, v) in d.OrderByDescending(x => x.Value)) Console.WriteLine($"   {k,-24} {v,4}"); Console.WriteLine($"   TOTAL {d.Values.Sum()}"); }
        decimal Sum(string sql) { using var cmd = c.CreateCommand(); cmd.CommandText = sql; return Convert.ToDecimal(cmd.ExecuteScalar() ?? 0, System.Globalization.CultureInfo.InvariantCulture); }
        string Fingerprint() => string.Join("|", new[] { "SELECT COUNT(*) FROM scheduled_orders", "SELECT COUNT(*) FROM sales", "SELECT COUNT(*) FROM cash_sessions", "SELECT IFNULL(SUM(CAST(order_total AS REAL)),0) FROM scheduled_orders", "SELECT COUNT(*) FROM audit_log" }.Select(s => { try { return Sum(s).ToString(); } catch { return "?"; } }));
        var before = StageMigration.Count(c); Show("ANTES", before); var fp = Fingerprint(); Console.WriteLine("Impressão digital (pedidos|pagamentos|caixas): " + fp);
        var report = StageMigration.Apply(c, backups);
        if (report == null) { Console.WriteLine("Nada a converter."); return; }
        Show("DEPOIS", report.After); Console.WriteLine($"Pedidos convertidos: {report.Converted} | backup verificado: {(File.Exists(report.BackupPath) ? "sim" : "NAO")} ({Path.GetFileName(report.BackupPath)})");
        Console.WriteLine("Impressão digital depois: " + Fingerprint() + (Fingerprint() == fp ? "  (igual)" : "  (DIFERENTE!)"));
        var restored = StageMigration.Rollback(c); var back = StageMigration.Count(c); Show($"APÓS DESFAZER ({restored} restaurados)", back);
        Console.WriteLine("Desfazer devolveu exatamente a contagem original: " + (back.Count == before.Count && before.All(x => back.GetValueOrDefault(x.Key) == x.Value)));
        var skipped = StageMigration.Apply(c, backups) == null; Console.WriteLine("Depois de desfazer, não reconverte sozinho: " + skipped); var again = StageMigration.Reapply(c, backups); Console.WriteLine("Converter de novo (manual): " + (again != null ? $"ok ({again.Converted})" : "nada a fazer"));
    }
    // Modo --medir: tempo (mediana de 5) e tamanho de cada resposta do servidor, na cópia do banco, pela rede local (127.0.0.1).
    // Estima também quanto levaria em redes lentas (só transferência; o atraso de ida e volta é somado à parte).
    static async Task Measure(DataStore store, DataStore local)
    {
        var data = local.Load();
        var order = data.ScheduledOrders.OrderByDescending(x => x.Number).First();
        var rich = data.ScheduledOrders.OrderByDescending(x => x.Payments.Count(p => p.SaleId != null) + x.Items.Count(i => i.ProductId != null)).ThenBy(x => x.Number).First();
        var closed = data.Sessions.Where(x => !x.IsOpen).OrderByDescending(x => x.OpenedAt).First();
        var product = data.Products.First();
        string M(string module) => $"api/v1/{module}?projection=desktop&page=1&pageSize=30";
        var endpoints = new (string Name, string Path)[]
        {
            ("revisão (polling 30 s)", "api/v1/revision"), ("bootstrap (login)", "api/v1/bootstrap"),
            ("Início", M("dashboard")), ("Pedidos", M("pedidos")), ("Quadro", M("producao")), ("Clientes", M("clientes")), ("Produtos", M("produtos")),
            ("Compras", M("compras")), ("Lembretes", M("lembretes")), ("Pesquisar lançamentos", M("lancamentos")), ("Fechamentos", M("fechamentos")),
            ("Devedores", "desktop/receivables?losses=false&page=1&search=&filter=pending&status=&sort=old"),
            ("busca de produto", "desktop/lookup/products?search=adesivo"), ("busca de cliente", "desktop/lookup/customers?search=ana"),
            ("abrir pedido", $"desktop/context/order/{order.Id}"), ("abrir pedido pago c/ produtos", $"desktop/context/order/{rich.Id}"), ("contexto fiscal", $"desktop/context/fiscal/{rich.Id}"), ("contexto de produto do pedido", $"desktop/context/product/{rich.Id}"), ("contexto do caixa", $"desktop/edit/cash/{Guid.Empty}"),
            ("contexto de fechamento", $"desktop/edit/closed/{closed.Id}"), ("contexto de produto", $"desktop/edit/product/{product.Id}"),
            ("configurações", $"desktop/edit/settings/{Guid.Empty}"),
            ("Relatórios (mês)", M("relatorios/resumo")+$"&from={DateTime.Today:yyyy-MM}-01&to={DateTime.Today:yyyy-MM-dd}"), ("Relatórios (tudo)", M("relatorios/resumo")), ("Cancelados", "desktop/cancelled"),
            ("celular: Início", "api/v1/relatorios/operacional?period=today"), ("celular: Pedidos", "api/v1/pedidos?page=1&pageSize=20"), ("celular: pedido", $"api/v1/pedidos/{order.Id}"),
            ("celular: caixa", "api/v1/caixa/atual"), ("celular: lista de devedores", "api/v1/devedores?page=1&pageSize=50"), ("celular: painel", "api/v1/dashboard"), ("celular: pedidos ativos", "api/v1/producao?page=1&pageSize=20"), ("celular: Devedores", "api/v1/acompanhamento?page=1"), ("celular: resumo financeiro", $"api/v1/relatorios/resumo?from={DateTime.Today:yyyy-MM}-01&to={DateTime.Today:yyyy-MM-dd}"),
        };
        var rows = new List<string> { "tela;ms_mediana;ms_max;kb;kb_rede;estimativa_2Mbps_ms;estimativa_20Mbps_ms" };
        Console.WriteLine($"{"Tela / chamada",-28} {"ms",7} {"máx",7} {"KB",9} {"KB rede",9} {"2 Mbps",9} {"20 Mbps",9}");
        var filter = Array.IndexOf(args0, "--so-medir") is var fi && fi >= 0 && fi + 1 < args0.Length ? args0[fi + 1] : "";
        foreach (var (name, path) in endpoints.Where(x => filter.Length == 0 || x.Name.StartsWith(filter, StringComparison.OrdinalIgnoreCase)))
        {
            var times = new List<double>(); long bytes = 0, wire = 0; string error = "";
            for (var i = 0; i < 6; i++)
            {
                var sw = Stopwatch.StartNew();
                try { var json = await store.QueryRemoteAsync<JsonElement>(path); sw.Stop(); var raw = Encoding.UTF8.GetBytes(json.GetRawText()); bytes = raw.Length; wire = WireBytes(raw); if (i == 0 && args0.Contains("--despejar")) File.WriteAllBytes(Path.Combine(Directory.CreateDirectory(Path.Combine(output, "json")).FullName, string.Concat(name.Split(Path.GetInvalidFileNameChars())) + ".json"), raw); }
                catch (Exception ex) { error = ex.Message.Split('\n')[0]; if (args0.Contains("--detalhe")) Console.WriteLine(ex); break; }
                if (i > 0) times.Add(sw.Elapsed.TotalMilliseconds); // a 1ª chamada aquece o servidor
            }
            if (error.Length > 0) { Console.WriteLine($"{name,-28} ERRO {error}"); rows.Add($"{name};erro;;;;;"); continue; }
            times.Sort(); var median = times[times.Count / 2]; var max = times[^1]; var kb = bytes / 1024.0;
            var slow = wire * 8 / 2_000_000.0 * 1000; var fast = wire * 8 / 20_000_000.0 * 1000;
            Console.WriteLine($"{name,-28} {median,7:0} {max,7:0} {kb,9:0.0} {wire / 1024.0,9:0.0} {slow,9:0} {fast,9:0}");
            rows.Add($"{name};{median:0};{max:0};{kb:0.0};{wire / 1024.0:0.0};{slow:0};{fast:0}");
        }
        var load = new List<double>(); for (var i = 0; i < 4; i++) { var sw = Stopwatch.StartNew(); local.Load(); sw.Stop(); if (i > 0) load.Add(sw.Elapsed.TotalMilliseconds); }
        load.Sort(); Console.WriteLine($"{"leitura completa do banco",-28} {load[load.Count / 2],7:0}   (o servidor faz isto em muitas chamadas)");
        rows.Add($"leitura completa do banco (DataStore.Load);{load[load.Count / 2]:0};;;;;");
        File.WriteAllLines(Path.Combine(output, "medicao.csv"), rows, Encoding.UTF8);
    }
    // Bytes que passam na rede: com --comprimido, o que o servidor manda (Brotli rápido, como o ResponseCompression); sem, o JSON cru.
    static bool compressedWire;
    static long WireBytes(byte[] raw)
    {
        if (!compressedWire) return raw.Length;
        using var memory = new MemoryStream();
        using (var brotli = new System.IO.Compression.BrotliStream(memory, System.IO.Compression.CompressionLevel.Fastest, true)) brotli.Write(raw);
        return memory.Length;
    }
    // Modo --perfil-devedores: tempo de cada parte do cálculo de Devedores (só leitura da cópia).
    static void ProfileDebtors(DataStore local)
    {
        var d = local.Load();
        Console.WriteLine($"pedidos={d.ScheduledOrders.Count} entregues={d.ScheduledOrders.Count(x => x.Stage == "Entregue")} clientes={d.Customers.Count} dívidas avulsas={d.ManualDebts.Count} vendas={d.Sessions.Sum(x => x.Sales.Count)} contatos={d.CollectionContacts.Count} perdas={d.Prompt12.Losses.Count} correções={d.FinancialCorrections.Count}");
        double T(Action a, int n = 5) { a(); var sw = Stopwatch.StartNew(); for (var i = 0; i < n; i++) a(); return sw.Elapsed.TotalMilliseconds / n; }
        Console.WriteLine($"Rows(pendentes)          {T(() => ReceivableViews.Rows(d, false)),8:0.0} ms");
        Console.WriteLine($"Rows(perdas)             {T(() => ReceivableViews.Rows(d, true)),8:0.0} ms");
        var delivered = d.ScheduledOrders.Where(x => !x.IsUncollected && x.Stage == "Entregue").ToList();
        Console.WriteLine($"  Customer() x pedidos   {T(() => { foreach (var o in delivered) ReceivableViews.Customer(d, o.CustomerId, o.CustomerName, o.Contact); }),8:0.0} ms");
        Console.WriteLine($"  Status() x pedidos     {T(() => { foreach (var o in delivered) CollectionControl.Status(d, o.Id, true); }),8:0.0} ms");
        Console.WriteLine($"  Remaining() x avulsas  {T(() => { foreach (var m in d.ManualDebts) m.Remaining(d); }),8:0.0} ms");
        Console.WriteLine($"  Status() x avulsas     {T(() => { foreach (var m in d.ManualDebts) CollectionControl.Status(d, m.Id, false); }),8:0.0} ms");
        var b = new DebtBalances(d); var diff = d.ManualDebts.Count(m => b.Paid(m) != m.Paid(d) || b.Closed(m) != m.IsClosedByCancellation(d) || b.Remaining(m) != m.Remaining(d) || b.Remaining(m).ToString(System.Globalization.CultureInfo.InvariantCulture) != m.Remaining(d).ToString(System.Globalization.CultureInfo.InvariantCulture) || b.Status(m.Id) != CollectionControl.Status(d, m.Id, false));
        var rows = ReceivableViews.Rows(d, false).Where(x => !x.Order).ToList(); var rowDiff = rows.Count(x => x.Balance != d.ManualDebts.First(m => m.Id == x.Id).Remaining(d) || x.CollectionStatus != CollectionControl.Status(d, x.Id, false));
        var expected = d.ManualDebts.Count(m => !m.IsClosedByCancellation(d));
        Console.WriteLine($"Conferência com a regra original: {d.ManualDebts.Count} dívidas, diferenças={diff}; linhas de Devedores={rows.Count} (esperado {expected}), diferenças={rowDiff}");
    }
    // Modo --perfil-celular: tempo de cada parte das consultas do celular (só leitura da cópia).
    static void ProfileMobile(DataStore local)
    {
        using var db = new SqliteConnection($"Data Source={local.DatabasePath};Mode=ReadOnly;Pooling=False"); db.Open();
        double T(Action a, int n = 5) { a(); var sw = Stopwatch.StartNew(); for (var i = 0; i < n; i++) a(); return sw.Elapsed.TotalMilliseconds / n; }
        var p = Permissions.Catalog.ToDictionary(x => x.Key, _ => true); var from = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1); var end = DateTime.Today.AddDays(1);
        var q = new WebQuery(1, 30, "", null, from, DateTime.Today);
        AppData? source = null;
        Console.WriteLine($"FinancialSource           {T(() => source = ReportQueries.FinancialSource(db, from, end)),8:0.0} ms");
        Console.WriteLine($"  ReadOrders(todos)       {T(() => WebQueries.ReadOrders(db, "")),8:0.0} ms");
        Console.WriteLine($"  Json order_details_v1   {T(() => WebQueries.Json<Dictionary<Guid, OrderDetails>>(db, "order_details_v1", "{}")),8:0.0} ms  ({WebQueries.Metadata(db, "order_details_v1", "").Length / 1024} KB)");
        Console.WriteLine($"  Json sale_products_v1   {T(() => WebQueries.Json<Dictionary<Guid, Sale>>(db, "sale_products_v1", "{}")),8:0.0} ms  ({WebQueries.Metadata(db, "sale_products_v1", "").Length / 1024} KB)");
        Console.WriteLine($"  Details() x 1 pedido    {T(() => OperationalQueries.Details(db, source!.ScheduledOrders[0])),8:0.0} ms");
        Console.WriteLine($"FinancialOverview         {T(() => new FinancialOverview(source!, from, end)),8:0.0} ms");
        Console.WriteLine($"Summary                   {T(() => ReportQueries.Summary(db, p, q)),8:0.0} ms");
        Console.WriteLine($"Overview (Início celular) {T(() => OperationalQueries.Overview(db, p, q)),8:0.0} ms");
        Console.WriteLine($"Debts (devedores celular) {T(() => WebQueries.Debts(db, p)),8:0.0} ms");
    }
    // Modo --perfil-relatorios: consulta de Relatórios (este mês) com e sem os totais prontos: tempo, tamanho e leitura no PC.
    static async Task ProfileReports(DataStore store)
    {
        var from = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        for (var i = 0; i < 3; i++) { var c = Stopwatch.StartNew(); var list = await store.QueryRemoteAsync<List<CancelledRecord>>("desktop/cancelled"); Console.WriteLine($"cancelados {list.Count} em {c.ElapsedMilliseconds} ms"); }
        foreach (var extra in new[] { "", "&totais=1", "", "&totais=1", "", "&totais=1" })
        {
            var sw = Stopwatch.StartNew(); var page = await store.QueryRemoteAsync<ModulePage>($"api/v1/relatorios/resumo?projection=desktop&page=1&pageSize=30&from={from:yyyy-MM-dd}&to={DateTime.Today:yyyy-MM-dd}" + extra); var query = sw.ElapsedMilliseconds;
            var size = page.Data.GetRawText().Length; sw.Restart(); var data = page.Data.Deserialize<AppData>()!; var read = sw.ElapsedMilliseconds;
            sw.Restart(); _ = new FinancialOverview(data, from, DateTime.Today.AddDays(1)); var overview = sw.ElapsedMilliseconds;
            Console.WriteLine($"{(extra.Length == 0 ? "completo" : "totais=1"),-9} consulta {query,5} ms | {size / 1024,6} KB | ler {read,4} ms | FinancialOverview {overview,4} ms");
            Console.WriteLine("  " + string.Join(" | ", page.Data.EnumerateObject().Select(p => $"{p.Name} {p.Value.GetRawText().Length / 1024} KB")));
        }
    }
    // Modo --fases: só leitura da cópia do banco; conta pedidos por etapa (levantamento do pacote 9).
    static void CountStages(DataStore local)
    {
        var d = local.Load(); var o = d.ScheduledOrders;
        Console.WriteLine("== Pedidos por etapa (todos) =="); foreach (var g in o.GroupBy(x => x.Stage).OrderByDescending(g => g.Count())) Console.WriteLine($"{g.Key,-24} {g.Count(),5}   (cancelados/arquivados: {g.Count(x => x.IsUncollected)})");
        Console.WriteLine($"Total de pedidos: {o.Count}");
        Console.WriteLine($"Imediato (prazo 0 dia útil) ainda não entregue: {o.Count(x => x.ProductionBusinessDays == 0 && !x.IsUncollected && x.Stage != "Entregue")}");
        Console.WriteLine($"Entregues com saldo a receber: {o.Count(x => x.Stage == "Entregue" && x.RemainingAmount > 0 && !x.IsUncollected)}");
        Console.WriteLine($"Entregues com valor a definir: {o.Count(x => x.Stage == "Entregue" && x.PricePending)}");
        Console.WriteLine($"Prazo ainda não iniciado (aguardando aprovação) por etapa:"); foreach (var g in o.Where(x => !x.DeadlineStarted && !x.IsUncollected).GroupBy(x => x.Stage)) Console.WriteLine($"   {g.Key,-24} {g.Count()}");
        Console.WriteLine($"Eventos de auditoria que citam etapa: {d.AuditTrail?.Count(a => a.Action.Contains("Etapa") || a.Description.Contains("etapa")) ?? 0}");
        Console.WriteLine($"Lembretes automáticos ligados a pedidos: {d.Reminders?.Count ?? 0} lembrete(s) no total");
    }
    // Modo --pdfs <pasta>: gera os PDFs atuais (linha de base) a partir da cópia do banco, para comparar antes/depois.
    static void GeneratePdfs(DataStore local, string folder)
    {
        Directory.CreateDirectory(folder);
        var data = local.Load();
        ReportWriter.OutputFolderResolver = () => folder; ReportWriter.OutputFolder = folder;
        ReportWriter.OrderLinkBase = "https://pc1:5444/app/#pedidos/"; // exemplo de endereço do celular (QR da ficha)
        string Try(string name, Func<string> make) { try { var path = make(); var target = Path.Combine(folder, name); if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) File.Copy(path, target, true); Console.WriteLine("ok " + name); return target; } catch (Exception ex) { Log($"PDF {name}: {ex.Message}"); return ""; } }
        var closed = data.Sessions.Where(x => !x.IsOpen).OrderByDescending(x => x.OpenedAt).FirstOrDefault();
        if (closed != null) Try("fechamento.pdf", () => ReportWriter.Write(closed, data.CashClosureVersions.Where(v => v.SessionId == closed.Id).OrderBy(v => v.Version).LastOrDefault()));
        var orders = data.ScheduledOrders.Where(o => !o.IsUncollected && o.Payments.Count > 0 && o.Items.Count >= 2).OrderByDescending(o => o.Number).Take(2).ToList();
        if (orders.Count > 0) Try("ficha-pedido.pdf", () => ReportWriter.WriteOrderSheet(orders, Path.Combine(folder, "ficha-pedido.pdf")));
        if (orders.Count > 0) Try("recibo-pedido.pdf", () => ReportWriter.WriteReceipt(orders[0], Path.Combine(folder, "recibo-pedido.pdf")));
        Try("recibo-avulso.pdf", () => ReportWriter.WriteStandaloneReceipt(new StandaloneReceipt("261012-1030", "Cliente Exemplo Ltda", "12.345.678/0001-90", 1234.56m, "Impressão de 500 folders A5", "PIX", DateTime.Today, "São Paulo", "Pagamento integral."), Path.Combine(folder, "recibo-avulso.pdf")));
    }

    static DataStore? localStore;
    static async Task CaptureAll(DataStore store, string suffix)
    {
        await Shot("00-login", suffix, () => new LoginWindow(), null);

        var main = new MainWindow(store) { ShowInTaskbar = false, ShowActivated = false };
        main.WindowState = WindowState.Normal; main.WindowStartupLocation = WindowStartupLocation.Manual;
        main.Left = -30000; main.Top = 0; main.Width = width; main.Height = height;
        var home = Stopwatch.StartNew();
        main.Show();
        await Until(() => Text(main, "LastUpdateText").StartsWith("Atualizado") || Text(main, "LastUpdateText").StartsWith("Falha"), 30000);
        await Idle(); home.Stop(); times.Add($"{suffix};01-inicio;{home.ElapsedMilliseconds}");
        if (measureOnly)
        {
            // Início de novo, já aquecido (como ao voltar da outra tela): atualizar e esperar o "Atualizado".
            for (var i = 0; i < 3; i++) { var warm = Stopwatch.StartNew(); Invoke(main, "RefreshData", false, false); await Until(() => Text(main, "LastUpdateText").StartsWith("Atualizado") || Text(main, "LastUpdateText").StartsWith("Falha"), 30000); await Idle(); Console.WriteLine($"{"01-inicio (atualizar)",-28} {warm.ElapsedMilliseconds,5} ms"); }
            Console.WriteLine($"{"01-inicio (abrir)",-28} {home.ElapsedMilliseconds,5} ms");
        }
        if (Wanted("01-inicio")) Save(main, "01-inicio", suffix);
        if (Wanted("01h"))
        {
            var todayTab = (RadioButton)main.FindName("TodayTab"); var ordersTab = (RadioButton)main.FindName("OrdersTab"); var todayList = (ListBox)main.FindName("TodayList");
            todayTab.IsChecked = true; Invoke(main, "QueueTab_Click", main, new RoutedEventArgs()); await Until(() => todayList.Items.Count > 0 || ((FrameworkElement)main.FindName("TodayEmpty")).Visibility == Visibility.Visible, 15000); await Idle(); Save(main, "01h-inicio-hoje", suffix);
            ordersTab.IsChecked = true; Invoke(main, "QueueTab_Click", main, new RoutedEventArgs());
        }
        // Aviso de certificado perto de vencer (só na cópia do banco): aparece para quem cuida do sistema e some quando renova.
        if (Wanted("01e") && localStore != null)
        {
            localStore.WriteMetadataValue(BrowserCertificateRenewer.StatusKey, JsonSerializer.Serialize(new BrowserCertificateStatus(DateTime.UtcNow.AddDays(12), DateTime.UtcNow, null, "teste")));
            localStore.Update(d => 0); // o Início só recarrega quando a revisão dos dados muda
            Invoke(main, "Home_Click", main, new RoutedEventArgs()); await Until(() => ((FrameworkElement)main.FindName("CertificateBox")).Visibility == Visibility.Visible, 15000); await Idle(); Save(main, "01e-inicio-certificado", suffix);
            localStore.WriteMetadataValue(BrowserCertificateRenewer.StatusKey, "null");
        }
        // Data comemorativa: aviso de 60 dias (pega o Natal) só para a captura; o aviso e a janela usam os dados reais do ano passado.
        if (Wanted("01c") || Wanted("01d"))
        {
            var lead = BusinessRules.Current.SeasonalLeadDays; BusinessRules.Current.SeasonalLeadDays = 60;
            try
            {
                typeof(MainWindow).GetField("seasonDay", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(main, DateTime.MinValue); Invoke(main, "LoadSeason");
                await Until(() => ((FrameworkElement)main.FindName("SeasonBox")).Visibility == Visibility.Visible, 15000); await Idle(); Save(main, "01c-inicio-data-comemorativa", suffix);
                if (typeof(MainWindow).GetField("season", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main) is SeasonInsight insight) await Shot("01d-data-comemorativa-sugestoes", suffix, () => new SeasonWindow(store, insight), main);
            }
            finally { BusinessRules.Current.SeasonalLeadDays = lead; }
        }
        if (Wanted("01-inicio") && main.FindName("NewMenuButton") is Button nb) { Invoke(main, "ShowNewMenu", nb); await Task.Delay(400); await Idle(); if (Field(main, "newMenu") is System.Windows.Controls.Primitives.Popup pop && pop.Child is FrameworkElement card) { card.UpdateLayout(); SaveElement(card, "01-inicio-menu-novo", suffix); pop.IsOpen = false; } }

        var pages = new (string Name, string Handler)[]
        {
            ("02-pedidos", "ScheduledOrders_Click"), ("03-orcamentos", "Budget_Click"), ("04-compras", "PurchaseNeeds_Click"), ("15-fornecedores", "Suppliers_Click"),
            ("05-produtos", "Products_Click"), ("06-clientes", "Customers_Click"), ("07-lembretes", "Reminders_Click"),
            ("08-devedores", "Debtors_Click"), ("09-fechamentos", "ClosedSessions_Click"), ("10-pesquisar-lancamentos", "Search_Click"),
            ("11-relatorios", "Reports_Click"), ("12-historico-backups", "History_Click"),
            ("13-sistema", "System_Click"), ("14-pedidos-quadro", "Quadro"), ("16-orcamentos-acompanhamento", "Prospects"),
        };
        var navigator = (PageNavigator)Field(main, "pages")!;
        foreach (var (name, handler) in pages)
        {
            if (!Wanted(name)) continue;
            try
            {
                var sw = Stopwatch.StartNew();
                if (handler == "Quadro") Invoke(main, "Navigate", (Button)main.FindName("NavOrders"), new Func<Window>(() => new OrderBoardWindow(store)));
                else if (handler == "Prospects") Invoke(main, "OpenProspects");
                else Invoke(main, handler, main, new RoutedEventArgs());
                await Until(() => navigator.Current != null, 10000);
                await WaitLoaders(navigator.Current!);
                await Idle();
                sw.Stop(); times.Add($"{suffix};{name};{sw.ElapsedMilliseconds}");
                if (measureOnly)
                {
                    // Segunda abertura (quente): o uso normal do dia, já com o programa aquecido.
                    Invoke(main, "Home_Click", main, new RoutedEventArgs()); await Idle();
                    var warm = Stopwatch.StartNew();
                    if (handler == "Quadro") Invoke(main, "Navigate", (Button)main.FindName("NavOrders"), new Func<Window>(() => new OrderBoardWindow(store)));
                else if (handler == "Prospects") Invoke(main, "OpenProspects");
                    else Invoke(main, handler, main, new RoutedEventArgs());
                    var built = warm.ElapsedMilliseconds;
                    await Until(() => navigator.Current != null, 10000); await WaitLoaders(navigator.Current!); var loaded = warm.ElapsedMilliseconds; await Idle();
                    times.Add($"{suffix};{name} (2a vez);{warm.ElapsedMilliseconds}");
                    Console.WriteLine($"{name,-28} construir {built,5} ms | dados {loaded - built,5} ms | desenhar {warm.ElapsedMilliseconds - loaded,5} ms");
                    if (name == "11-relatorios" && args0.Contains("--numeros")) await DumpReports((ReportsWindow)navigator.Current!);
                    continue;
                }
                Save(main, name, suffix);
                if (name == "02-pedidos" && main.FindName("PageHost") is ContentControl ordersHost && Descendants(ordersHost).OfType<DataGrid>().FirstOrDefault(g => g.IsVisible) is DataGrid ordersGrid && ordersGrid.Items.Count > 1) { ordersGrid.SelectedIndex = 1; await Task.Delay(400); await Idle(); Save(main, "02s-pedidos-selecionado", suffix); }
                // Lembretes com a pesquisa preenchida.
                if (name == "07-lembretes" && main.FindName("PageHost") is ContentControl remindersHost && Descendants(remindersHost).OfType<TextBox>().FirstOrDefault(SearchShortcut.GetIsTarget) is TextBox remindersSearch) { remindersSearch.Text = "banner"; await Task.Delay(400); await Idle(); Save(main, "07b-lembretes-pesquisa", suffix); remindersSearch.Text = "nada-encontrado"; await Task.Delay(400); await Idle(); Save(main, "07c-lembretes-sem-resultado", suffix); remindersSearch.Clear(); }
                // Orçamentos com 1,20 × 1,20 (valor arredondado para o real mais perto).
                if (name == "03-orcamentos" && navigator.Current is BudgetWindow budgetPage && budgetPage.FindName("WidthBox") is TextBox budgetWidth && budgetPage.FindName("HeightBox") is TextBox budgetHeight) { budgetWidth.Text = "1,20"; budgetHeight.Text = "1,20"; await Task.Delay(600); await Idle(); Save(main, "03b-orcamento-arredondado", suffix); }
                // Pedidos na aba Aguardando retirada (servidor, escopo "ready").
                if (name == "02-pedidos" && main.FindName("PageHost") is ContentControl readyHost && Descendants(readyHost).OfType<Button>().FirstOrDefault(b => b.Name == "ReadyButton") is Button readyTab) { readyTab.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, readyTab)); await Task.Delay(1500); await Idle(); Save(main, "02r-pedidos-retirada", suffix); }
                // Clientes com uma ficha aberta (tipo de preço, totais e histórico).
                if (name == "06-clientes" && main.FindName("PageHost") is ContentControl host && Descendants(host).OfType<DataGrid>().FirstOrDefault(g => g.IsVisible) is DataGrid customers && customers.Items.Count > 5) { customers.SelectedIndex = 5; await Task.Delay(600); await Idle(); Save(main, "06b-clientes-ficha", suffix); }
                // Produtos em cards com miniatura da foto (busca "caneca", onde há produto com foto).
                if (name == "05-produtos" && navigator.Current is ProductsWindow productsPage && productsPage.FindName("SearchBox") is TextBox productSearch) { productSearch.Text = "caneca"; await Task.Delay(1500); await WaitLoaders(productsPage); await Task.Delay(1500); await Idle(); Save(main, "05b-produtos-cards-foto", suffix); }
                // Catálogo para WhatsApp: 1ª página da categoria com mais fotos (fotos reais, logo e empresa do cadastro).
                if (name == "05-produtos")
                {
                    var all = await CatalogWindow.LoadProductsAsync(store);
                    var category = all.Where(x => x.Active && !string.IsNullOrWhiteSpace(x.PhotoPath)).GroupBy(x => x.Category).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key;
                    if (category != null)
                    {
                        var entries = new List<WhatsAppCatalog.Entry>();
                        foreach (var p in all.Where(x => x.Active && x.Category == category).OrderBy(x => x.Description)) entries.Add(new(p, string.IsNullOrWhiteSpace(p.PhotoPath) ? null : await ProductPhotos.LoadAsync(store, p.Id, 0, p.PhotoPath!, 600)));
                        var (company, logo) = CatalogWindow.CompanyAndLogo(store);
                        var page = WhatsAppCatalog.Render(WhatsAppCatalog.Pages(company, logo, category, entries, DateTime.Today)[0]);
                        var dir = Path.Combine(output, suffix); Directory.CreateDirectory(dir); File.WriteAllBytes(Path.Combine(dir, "05c-catalogo-whatsapp.jpg"), page.Jpeg); Log($"ok {suffix}/05c-catalogo-whatsapp.jpg ({category})");
                    }
                }
                if (name == "13-sistema" && Descendants(main).OfType<ListBox>().FirstOrDefault(l => l.IsVisible) is ListBox groups && groups.Items.Count > 3) { groups.SelectedIndex = 3; await Idle(); Save(main, "13-sistema-usuarios", suffix); groups.SelectedIndex = 1; await Idle(); Save(main, "13-sistema-geral", suffix); }
                // Abas (Relatorios, etc.): uma captura por aba.
                var tabs = Descendants(main).OfType<TabControl>().FirstOrDefault(t => t.IsVisible);
                if (tabs != null)
                    for (int i = 1; i < tabs.Items.Count; i++)
                    {
                        tabs.SelectedIndex = i; await WaitLoaders(navigator.Current!); await Idle();
                        var header = (tabs.Items[i] as TabItem)?.Header?.ToString() ?? i.ToString();
                        Save(main, $"{name}-aba{i}-{Slug(header)}", suffix);
                    }
            }
            catch (Exception ex) { Log($"{name} [{suffix}]: {ex.GetType().Name}: {ex.Message}"); }
        }
        Invoke(main, "Home_Click", main, new RoutedEventArgs());
        await Idle();

        // Diálogos (abertos sem ShowDialog, fora da tela, apenas para a captura).
        var orderRows = (System.Collections.IEnumerable)Field(main, "orderRows")!;
        var firstOrderId = orderRows.Cast<DashboardOrderRow>().Select(x => (Guid?)x.Id).FirstOrDefault();
        ScheduledOrder? order = null;
        if (firstOrderId != null) order = (await store.QueryRemoteAsync<AppData>($"desktop/context/order/{firstOrderId}")).ScheduledOrders.FirstOrDefault(x => x.Id == firstOrderId);
        var cash = await store.BusinessContextAsync("cash");
        var open = cash.Sessions.LastOrDefault(x => x.IsOpen);

        await Shot("20-novo-lancamento", suffix, () => new SaleWindow(store: store), main);
        await Shot("20b-aviso-nome-numero", suffix, () => { var w = new SaleWindow(store: store); ((TextBox)typeof(SaleWindow).GetField("name", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(w)!).Text = "6616"; return w; }, main);
        await Shot("21-novo-pedido", suffix, () => new ScheduledOrderDialog(store), main);
        if (order != null)
        {
            await Shot("22-editar-pedido", suffix, () => new ScheduledOrderDialog(order, store), main);
            await Shot("23-fiscal-do-pedido", suffix, () => new OrderFiscalWindow(store, order), main);
            await Shot("23e-retrabalho-terceiros", suffix, () => new OrderExtrasWindow(store, order), main);
            await Shot("23r-registrar-retrabalho", suffix, () => new ReworkDialog(null, true), main);
            await Shot("23o-registrar-envio", suffix, () => new OutsourcedDialog(null, ["Fornecedor exemplo"], true), main);
            Window Hosted(FrameworkElement view) => new() { Title = "Prévia", Width = 1100, Height = 640, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = new Grid { Margin = new Thickness(24), Children = { view } } };
            await Shot("23m-limite-mei", suffix, () => { var v = new MeiLimitView(); v.Render(new MeiStatus(DateTime.Today.Year, 81000, 31250.50m, 42, 66480.75m, 61200m, DateTime.Today.AddDays(40), null)); return Hosted(v); }, main);
            await Shot("23p-retrabalho-lista", suffix, () => { var v = new ReworkListView(); v.Render([new ReworkRow(DateTime.Today, 192, "Cliente exemplo", "Corte torto", 200m), new ReworkRow(DateTime.Today.AddDays(-40), 150, "Outro cliente", "Impressão com cor errada", 85.5m)]); return Hosted(v); }, main);
            await Shot("23d-resumo-do-dia", suffix, () => Hosted(new DaySummaryView { Store = store }), main);
            await Shot("24-receber-pagamento", suffix, () => new PaymentReceivedWindow(order), main);
        }
        await Shot("25-novo-produto", suffix, () => new ProductEditor(null, DocumentPaths.Root(store), store), main);
        if (Wanted("25"))
        {
            var page = await store.QueryRemoteAsync<ModulePage>("api/v1/produtos?projection=desktop&page=1&pageSize=60");
            var products = page.Data.Deserialize<AppData>()!.Products;
            var tiered = products.FirstOrDefault(x => x.UseQuantityPricing && x.QuantityPriceTiers.Count > 1);
            var exact = products.FirstOrDefault(x => x.ExactQuantityPrices.Count > 1);
            var simple = products.FirstOrDefault(x => x.Cost != null);
            if (tiered != null) await Shot("25b-editar-produto-faixas", suffix, () => new ProductEditor(tiered, DocumentPaths.Root(store), store), main);
            if (exact != null) await Shot("25c-editar-produto-exato", suffix, () => new ProductEditor(exact, DocumentPaths.Root(store), store), main);
            if (simple != null) await Shot("25d-editar-produto-simples", suffix, () => new ProductEditor(simple, DocumentPaths.Root(store), store), main);
            // 3 fotos (cores lisas no lugar de fotos reais): Principal, "Tornar principal" e remover.
            if (simple != null) await Shot("25e-editar-produto-3-fotos", suffix, () =>
            {
                var editor = new ProductEditor(simple, DocumentPaths.Root(store), store);
                foreach (var color in new[] { (90, 160, 220), (120, 200, 150), (230, 170, 90) })
                {
                    var pixels = new byte[800 * 600 * 4]; for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = (byte)color.Item3; pixels[i + 1] = (byte)color.Item2; pixels[i + 2] = (byte)color.Item1; pixels[i + 3] = 255; }
                    var image = BitmapSource.Create(800, 600, 96, 96, PixelFormats.Bgra32, null, pixels, 800 * 4);
                    Call(editor, "AddPhoto", new Func<byte[]?>(() => ProductPhotos.Optimize(image)));
                }
                return editor;
            }, main);
            await Shot("25f-catalogo-whatsapp", suffix, () => new CatalogWindow(store, products), main);
        }        if (Wanted("21r")) { var repeated = store.Load().ScheduledOrders.Where(x => x.Items.Count > 0 && !x.IsCancelled).OrderByDescending(x => x.Number).FirstOrDefault(); if (repeated != null) await Shot("21r-repetir-pedido", suffix, () => { var w = new ScheduledOrderDialog(OrderRepeat.Draft(repeated), store); ((TextBlock)w.FindName("TitleText")).Text = $"Repetir pedido #{repeated.Number}"; return w; }, main); }
        await Shot("26-novo-cliente", suffix, () => new CustomerEditor(new CustomerProfile()), main);
        await Shot("27-recibo-avulso", suffix, () => new ReceiptDialog(store), main);
        await Shot("28-abrir-caixa", suffix, () => new AmountWindow("Abrir caixa", "Valor inicial em dinheiro:"), main);
        { var sample = open ?? new CashSession { OpeningAmount = 100, OpenedBy = "Exemplo" };
        {
            await Shot("29-fechar-caixa", suffix, () => new CashClosingWindow(sample), main);
            await Shot("29b-fechar-caixa-ajuste", suffix, () => { var copy = JsonSerializer.Deserialize<CashSession>(JsonSerializer.Serialize(sample))!; copy.Adjustments.Add(new CashAdjustment { Amount = 36, Reason = "Sangria de ontem não registrada", UserName = "Grafica" }); return new CashClosingWindow(copy); }, main);
            await Shot("30-sangria", suffix, () => new WithdrawalWindow(store, sample), main);
            await Shot("30b-ajuste-caixa", suffix, () => new CashAdjustmentWindow(store, sample), main);
        } }
        var settings = await store.BusinessContextAsync("settings");
        await Shot("31-configuracoes", suffix, () => new SettingsWindow(store, settings), main);
        await Shot("32-usuarios", suffix, () => new UsersWindow(store), main);
        await Shot("33-permissoes-financeiro", suffix, () => new PermissionsWindow(store, null, "Financeiro", "Financeiro"), main);
        await Shot("34-sessoes-dispositivos", suffix, () => new DevicesWindow(store), main);
        await Shot("35-auditoria", suffix, () => new AuditWindow(store), main);
        await Shot("36-trocar-senha", suffix, () => new ChangePasswordWindow(store, App.CurrentUser.UserName), main);
        await Shot("37-lancamentos-perdidos", suffix, () => new LostEntriesWindow(store), main);
        await Shot("38-lancamento-perdido-novo", suffix, () => new LostEntryDialog(), main);
        await Shot("39-caderno-melhorias", suffix, () => new ImprovementNotesWindow(store), main);
        await Shot("40-painel-visual", suffix, () => new OrderBoardWindow(store), main);
        await Shot("41-historico-pedidos", suffix, () => new OrderHistoryWindow(store), main);
        await Shot("42-categorias-produto", suffix, () => new ProductCategoriesWindow(store), main);
        await Shot("43-calculadora-producao", suffix, () => new ProductionCalculatorWindow(store), main);
        { var budgetData = await store.BusinessContextAsync("budget"); await Shot("44-parametros-orcamento", suffix, () => new BudgetSettingsWindow(store, budgetData), main); }
        await Shot("45-previa-orcamento", suffix, () => new BudgetPreviewWindow(store), main);
        await Shot("46-modelos-mensagem", suffix, () => new CommunicationTemplatesWindow(store), main);
        await Shot("47-vincular-cliente", suffix, () => new CustomerLinkDialog(store), main);
        await Shot("48-nova-necessidade-compra", suffix, () => Make("NeedDialog", (object?)null, (object?)null, (object?)null, (object?)null), main);
        await Shot("49-nova-divida", suffix, () => new FollowUpEditor(true, null, null), main);
        await Shot("50-nova-despesa", suffix, () => Make("ExpenseDialog", (object?)null), main);
        await Shot("51-motivo", suffix, () => new ReasonWindow("Cancelar lançamento", "O lançamento original será preservado e receberá um ajuste de cancelamento. Informe o motivo."), main);
        await Shot("52-alerta-confirmacao", suffix, () => new AlertWindow("Confirmar", "Deseja realmente continuar?", true), main);
        await Shot("53-declarar-perda", suffix, () => new LossDeclareDialog("Cliente", "Pedido #1", 120m), main);
        await Shot("54-pin-proprietario", suffix, () => Make("OwnerPinDialog", false, store), main);
        await Shot("54b-redefinir-pin", suffix, () => Make("OwnerPinResetDialog", store), main);
        await Shot("55-catalogo-componentes", suffix, () => new ComponentCatalogWindow(), main);
        await Shot("56-dialogo-exemplo", suffix, () => Make("SampleDialog"), main);
        await Shot("60-busca-ctrl-k", suffix, () => new CommandPalette((IReadOnlyList<AppCommand>)Call(main, "AllCommands")!), main);
        try { var ev = store.QueryAudit(new AuditFilter(DateTime.Today.AddDays(-60), DateTime.Today.AddDays(1))).FirstOrDefault(x => x.OldValue.Length > 2 && x.NewValue.Length > 2 && x.OldValue != x.NewValue); if (ev != null) await Shot("61-detalhe-auditoria", suffix, () => new AuditDetailDialog(new InvestigativeAuditRow(ev)), main); else Log("61: nenhum evento com antes/depois"); }
        catch (Exception ex) { Log($"61-detalhe-auditoria: {ex.Message}"); }
        try { await Shot("62-conectar-outro-pc", suffix, () => new ConnectionWindow(store.CreatePairing(), store.DatabasePath), main); } catch (Exception ex) { Log($"62-conectar-outro-pc: {ex.Message}"); }
        // Conciliação Pix: extrato OFX de teste montado com os PIX reais dos últimos 10 dias (um some, um cai no dia seguinte, um crédito sem lançamento e um repasse Getnet).
        try
        {
            var page=await store.QueryRemoteAsync<ModulePage>($"api/v1/lancamentos?projection=desktop&page=1&pageSize=100&payment=PIX&from={DateTime.Today.AddDays(-10):yyyy-MM-dd}&to={DateTime.Today:yyyy-MM-dd}");
            var pix=(page.Data.Deserialize<AppData>()?.Sessions??[]).SelectMany(x=>x.Sales).Where(x=>x.Total>0&&!x.Product.StartsWith("Ajuste do lançamento")).OrderBy(x=>x.CreatedAt).ToList();
            var ofx=new System.Text.StringBuilder("OFXHEADER:100\r\nDATA:OFXSGML\r\nCHARSET:1252\r\n\r\n<OFX><BANKTRANLIST>\r\n");void Trn(DateTime d,decimal v,string memo)=>ofx.Append($"<STMTTRN>\r\n<TRNTYPE>CREDIT\r\n<DTPOSTED>{d:yyyyMMdd}120000\r\n<TRNAMT>{v.ToString(System.Globalization.CultureInfo.InvariantCulture)}\r\n<MEMO>{memo}\r\n</STMTTRN>\r\n");
            for(int i=0;i<pix.Count;i++){if(i==1)continue;Trn(i==2?pix[i].CreatedAt.AddDays(1):pix[i].CreatedAt,pix[i].Total,"PIX RECEBIDO - "+pix[i].CustomerName.ToUpperInvariant());}
            Trn(DateTime.Today,77.70m,"PIX RECEBIDO - CLIENTE SEM LANCAMENTO");Trn(DateTime.Today,1500m,"GETNET VENDAS DEBITO");ofx.Append("</BANKTRANLIST></OFX>");
            var ofxPath=Path.Combine(Path.GetTempPath(),"uxcapture-extrato-teste.ofx");File.WriteAllBytes(ofxPath,System.Text.Encoding.Latin1.GetBytes(ofx.ToString()));
            await Shot("63-conciliacao-pix", suffix, () => { var w=new ReconciliationWindow(store); w.Loaded+=async(_,_)=>await w.LoadFileAsync(ofxPath); return w; }, main);
            await Shot("63b-conciliacao-pix-vazia", suffix, () => new ReconciliationWindow(store), main);
        }
        catch (Exception ex) { Log($"63-conciliacao-pix: {ex.Message}"); }
        try { await Shot("65-devolucao", suffix, () => Make("RefundWindow", new Sale { CustomerName = "Cliente Exemplo", Product = "30 Impressões A4 coloridas", Units = 1, TotalValue = 30, PaymentType = "Cartão de crédito 1x", CreatedAt = DateTime.Now.AddMinutes(-20) }, 30m), main); } catch (Exception ex) { Log($"65-devolucao: {ex.Message}"); }
        await Shot("66-novo-fornecedor", suffix, () => Make("SupplierDialog", (object?)null, (object?)"Lavoro Papéis"), main);
        await Shot("67-novo-lembrete", suffix, () => Make("ReminderDialog", (object?)null, (object?)null), main);
        await Shot("68-perfis-acesso", suffix, () => new ProfilesWindow(store), main);
        await Shot("69-lancamento-mais-vendido", suffix, () => { var w = new SaleWindow(store: store); w.Loaded += async (_, _) => { for (var i = 0; i < 40; i++) { await Task.Delay(100); var chips = (System.Collections.IList)typeof(SaleWindow).GetField("topChips", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(w)!; if (chips.Count == 0) continue; var first = chips[0]!; var product = (Product)first.GetType().GetField("Item2")!.GetValue(first)!; typeof(SaleWindow).GetMethod("UseTop", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(w, [product]); var row = ((System.Collections.IList)typeof(SaleWindow).GetField("items", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(w)!)[0]!; ((TextBox)row.GetType().GetProperty("Quantity")!.GetValue(row)!).Text = "150"; break; } }; return w; }, main);
        try { await Shot("64-whatsapp-em-sequencia", suffix, () => new WhatsAppQueueWindow("Avisar retiradas", "3 pedido(s) pronto(s) há 3 dia(s) ou mais · Esc fecha", "Enviei, próximo", [new("#1042 · Maria Souza", "Pronto há 5 dia(s) · saldo R$ 80,00", "11987651002", () => Task.FromResult("Olá, Maria Souza, tudo bem? Seu pedido #1042 está pronto para retirada desde 09/10. Estamos te esperando!\n\nPara pagar por Pix (R$ 80,00), copie o código abaixo e cole no app do banco:\n00020126...6304ABCD\n\n*Gráfica Rápida CtrlA CtrlP*")), new("#1043 · João", "Pronto há 4 dia(s)", "11999990000", () => Task.FromResult("Olá"))]), main); } catch (Exception ex) { Log($"64-whatsapp-em-sequencia: {ex.Message}"); }
        // Item em foco (foto do produto na lateral): 3 produtos com foto; e um produto sem foto (aviso no lugar da foto).
        var withPhoto = localStore?.Load().Products.Where(x => x.Active && !string.IsNullOrWhiteSpace(x.PhotoPath)).Take(3).ToList() ?? [];
        var noPhoto = localStore?.Load().Products.FirstOrDefault(x => x.Active && string.IsNullOrWhiteSpace(x.PhotoPath));
        Window SaleWith(IEnumerable<Product> products) { var w = new SaleWindow(store: store); w.Loaded += (_, _) => { foreach (var p in products) typeof(SaleWindow).GetMethod("UseTop", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(w, [p]); }; return w; }
        if (withPhoto.Count > 0) await Shot("73-lancamento-foto-do-item", suffix, () => SaleWith(withPhoto), main);
        if (noPhoto != null) await Shot("74-lancamento-item-sem-foto", suffix, () => SaleWith([noPhoto]), main);
        if (withPhoto.Count > 0) await Shot("75-pedido-foto-do-item", suffix, () => new ScheduledOrderDialog(new ScheduledOrder { Number = 9999, CustomerName = "Cliente Exemplo", Contact = "11952028478", OrderTotal = withPhoto.Sum(x => x.Price ?? 0), OriginalTotal = withPhoto.Sum(x => x.Price ?? 0), Items = withPhoto.Select(x => new OrderItem { ProductId = x.Id, Product = x.Description, Quantity = 1, UnitPrice = x.Price ?? 0 }).Append(new OrderItem { Product = "Arte personalizada", Quantity = 1, UnitPrice = 30 }).ToList() }, store), main);
        await Shot("70-atalhos", suffix, () => new AlertWindow("Atalhos do teclado", MainWindow.ShortcutsText), main);
        // Fluxo previsto (Área do proprietário, que pede PIN): o componente da aba, com os dados da cópia do banco.
        if (localStore != null) await Shot("72-fluxo-previsto", suffix, () => { var view = new CashForecastView(); var w = new Window { Title = "Fluxo previsto", Width = 1180, Height = 760, Content = new Border { Padding = new Thickness(24, 6, 24, 20), Child = view } }; w.Loaded += (_, _) => view.Render(CashForecast.Build(localStore.Load(), DateTime.Today)); return w; }, main);
        await Shot("58-item-fora-do-cadastro", suffix, () => new UnregisteredItemDialog(new UnregisteredItems.Pending("Banner 90x120 lona", 2, 85m), 1, 2), main);
        await Shot("59-escolher-produto", suffix, () => new ProductPickerDialog(store, "adesivo", 1), main);
        main.Close();
        await Idle();
    }

    // --fornecedores: cadastra (SÓ NA CÓPIA, pelo comando do servidor "supplier.save") fornecedores com os nomes já digitados nas compras reais,
    // edita um (revisão esperada) e confere que nome repetido é recusado — teste de ponta a ponta do cadastro.
    // --medir-pedido (só na cópia): cronometra abrir, salvar e recarregar um pedido como a tela faz.
    static async Task MeasureOrderSave(DataStore local, DataStore store)
    {
        var order0 = local.Load().ScheduledOrders.Where(x => x.Stage != "Entregue" && !x.IsUncollected).OrderByDescending(x => x.CreatedAt).First();
        for (var round = 1; round <= 3; round++)
        {
            var sw = Stopwatch.StartNew();
            var ctx = await store.QueryRemoteAsync<AppData>($"desktop/context/order/{order0.Id}"); var t1 = sw.ElapsedMilliseconds;
            var order = ctx.ScheduledOrders.Single(x => x.Id == order0.Id); order.Notes = (order.Notes ?? "") + (round % 2 == 0 ? "" : " ");
            await store.RemoteDesktopWriteAsync("desktop/orders/save", new DesktopOrderCommand(Guid.NewGuid(), order.Revision, order)); var t2 = sw.ElapsedMilliseconds;
            var page = await store.QueryRemoteAsync<ModulePage>("api/v1/pedidos?projection=desktop&page=1&pageSize=30&scope=active"); var data = page.Data.Deserialize<AppData>()!; var t3 = sw.ElapsedMilliseconds;
            var dash = await store.QueryRemoteAsync<ModulePage>("api/v1/dashboard?projection=desktop&page=1&pageSize=30"); var t4 = sw.ElapsedMilliseconds;
            Log($"MEDIR rodada {round}: abrir pedido {t1} ms | salvar {t2 - t1} ms | lista pedidos {t3 - t2} ms | início {t4 - t3} ms | total {t4} ms");
        }
    }
    // --medir-gravacoes (só na cópia): cronometra gravações de outras telas (produto, lembrete, fornecedor).
    static async Task MeasureWrites(DataStore local, DataStore store)
    {
        for (var round = 1; round <= 2; round++)
        {
            var product = local.Load().Products.First(x => x.Active);
            var sw = Stopwatch.StartNew(); await store.RemoteDesktopWriteAsync("desktop/products", new DesktopProductCommand(Guid.NewGuid(), DataStore.DesktopRevision(product), product)); var tProduct = sw.ElapsedMilliseconds;
            var reminder = new Reminder { Title = "Medição", Description = "teste", CreatedBy = "captura" };
            sw.Restart(); await store.RemoteDesktopWriteAsync("desktop/reminders", new DesktopReminderCommand(Guid.NewGuid(), reminder.Id, null, reminder)); var tReminder = sw.ElapsedMilliseconds;
            var supplier = new Supplier { Name = "Medição " + Guid.NewGuid().ToString("N")[..6] };
            sw.Restart(); await store.RemoteBusinessAsync("supplier.save", supplier.Id, null, new SupplierArgs(supplier)); var tSupplier = sw.ElapsedMilliseconds;
            sw.Restart(); var audit = store.QueryAudit(new(DateTime.Today.AddDays(-30), DateTime.Today.AddDays(1), "", "", "", "", "")); var tAudit = sw.ElapsedMilliseconds;
            Log($"MEDIR rodada {round}: produto {tProduct} ms | lembrete {tReminder} ms | fornecedor {tSupplier} ms | auditoria (consulta 30 dias, {audit.Count} eventos) {tAudit} ms");
        }
    }
    static async Task SeedSuppliers(DataStore local, DataStore store)
    {
        var d = local.Load();
        var names = d.Prompt12.Purchases.Where(x => x.Supplier.Trim().Length > 0).GroupBy(x => SupplierBook.Key(x.Supplier)).OrderByDescending(g => g.Count()).Take(5).Select(g => g.First().Supplier.Trim()).ToList();
        if (names.Count == 0) names = ["Fornecedor exemplo"];
        foreach (var name in names)
        {
            var mats = SupplierBook.Purchases(d.Prompt12).Where(x => SupplierBook.Key(x.Supplier) == SupplierBook.Key(name)).Select(x => x.Material.Split(' ')[0]).Distinct().Take(3).ToList();
            var s = new Supplier { Name = name, Supplies = mats, LeadTime = "2 dias úteis", PaymentTerms = "PIX" };
            await store.RemoteBusinessAsync("supplier.save", s.Id, null, new SupplierArgs(s));
        }
        var saved = local.Load().Prompt12.Suppliers;
        if (saved.Count != names.Count) throw new InvalidOperationException($"Servidor gravou {saved.Count} de {names.Count} fornecedores.");
        var first = saved.First(); var edited = System.Text.Json.JsonSerializer.Deserialize<Supplier>(System.Text.Json.JsonSerializer.Serialize(first))!; edited.WhatsApp = "11999990000";
        await store.RemoteBusinessAsync("supplier.save", first.Id, DataStore.DesktopRevision(first), new SupplierArgs(edited));
        if (local.Load().Prompt12.Suppliers.Single(x => x.Id == first.Id).WhatsApp != "11999990000") throw new InvalidOperationException("Edição do fornecedor não gravou.");
        var copy = new Supplier { Name = first.Name.ToUpperInvariant() }; var duplicated = false; try { await store.RemoteBusinessAsync("supplier.save", copy.Id, null, new SupplierArgs(copy)); } catch (Exception ex) { duplicated = ex.Message.Contains("Já existe"); }
        if (!duplicated) throw new InvalidOperationException("Servidor aceitou fornecedor com nome repetido.");
        Log($"Fornecedores pelo servidor (só na cópia): {string.Join(", ", names)} · edição e recusa de nome repetido conferidas");
    }

    static Window Make(string type, params object?[] args) => (Window)Activator.CreateInstance(typeof(App).Assembly.GetTypes().First(t => t.Name == type), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, args, null)!;
    static void Log(string line) { log.Add(line); Console.WriteLine(line); Console.Out.Flush(); }
    static bool Wanted(string name) => only.Length == 0 || name.StartsWith(only, StringComparison.OrdinalIgnoreCase);

    static async Task Shot(string name, string suffix, Func<Window> create, Window? owner)
    {
        if (!Wanted(name)) return;
        Window? w = null;
        try
        {
            var sw = Stopwatch.StartNew();
            w = create();
            if (owner != null) w.Owner = owner;
            w.ShowInTaskbar = false; w.ShowActivated = false; w.WindowStartupLocation = WindowStartupLocation.Manual; w.Left = -30000; w.Top = 0;
            if (w.WindowState == WindowState.Maximized) { w.WindowState = WindowState.Normal; w.Width = width; w.Height = height; }
            w.Show();
            if (!measureOnly) await Task.Delay(300); await WaitLoaders(w); await Idle();
            sw.Stop(); times.Add($"{suffix};{name};{sw.ElapsedMilliseconds}");
            if (measureOnly) return;
            Save(w, name, suffix);
            if (w.FindName("Body") is StackPanel body && w is ComponentCatalogWindow) SaveElement(body, name + "-completo", suffix);
            var tabs = Descendants(w).OfType<TabControl>().FirstOrDefault(t => t.IsVisible);
            if (tabs != null)
                for (int i = 1; i < tabs.Items.Count; i++)
                {
                    tabs.SelectedIndex = i; await WaitLoaders(w); await Idle();
                    var header = (tabs.Items[i] as TabItem)?.Header?.ToString() ?? i.ToString();
                    Save(w, $"{name}-aba{i}-{Slug(header)}", suffix);
                }
        }
        catch (Exception ex) { Log($"{name} [{suffix}]: {ex.GetType().Name}: {(ex.InnerException ?? ex).Message}"); }
        finally { try { if (w != null) { if (UnsavedChanges.IsDirty(w)) Log($"SUJO ao fechar sem editar: {name}"); w.Close(); } } catch { } await Idle(); }
    }

    // --a11y: lista controles clicáveis sem nome para leitor de tela (nome calculado pelo próprio WPF/UI Automation).
    static void AuditA11y(DependencyObject root, string name)
    {
        var missing = new List<string>(); var total = 0;
        foreach (var c in Descendants(root).OfType<System.Windows.Controls.Primitives.ButtonBase>().Where(x => x.IsVisible))
        {
            total++; string peerName;
            try { peerName = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(c)?.GetName() ?? ""; } catch { peerName = ""; }
            if (string.IsNullOrWhiteSpace(peerName)) missing.Add($"{c.GetType().Name} tip={(c.ToolTip as string ?? "")} name=[{System.Windows.Automation.AutomationProperties.GetName(c)}] bound={System.Windows.Data.BindingOperations.IsDataBound(c, System.Windows.Automation.AutomationProperties.NameProperty)} loaded={c.IsLoaded}");
        }
        if (args0.Contains("--a11y-nomes")) Log($"NOMES {name}: " + string.Join(" | ", Descendants(root).OfType<System.Windows.Controls.Primitives.ButtonBase>().Where(x => x.IsVisible).Select(x => { try { return System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(x)?.GetName() ?? ""; } catch { return ""; } })));
        Log($"A11Y {name}: {total} botões, {missing.Count} sem nome" + (missing.Count > 0 ? " → " + string.Join(" | ", missing.Distinct().Take(8)) : ""));
    }
    static void Save(Window window, string name, string suffix)
    {
        var root = (FrameworkElement)(window.Content ?? window);
        root.UpdateLayout();
        if (args0.Contains("--a11y")) AuditA11y(root, name);
        // A margem do conteúdo entra no quadro (antes a borda direita/inferior saía cortada no print, sem estar cortada na tela).
        var w = Math.Max(1, (int)Math.Ceiling(root.ActualWidth + root.Margin.Left + root.Margin.Right)); var h = Math.Max(1, (int)Math.Ceiling(root.ActualHeight + root.Margin.Top + root.Margin.Bottom));
        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var dc = background.RenderOpen()) dc.DrawRectangle(window.Background is SolidColorBrush b && b.Color.A > 0 ? b : ThemeManager.Get("Paper"), null, new Rect(0, 0, w, h));
        bitmap.Render(background); bitmap.Render(root);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        var dir = Path.Combine(output, suffix); Directory.CreateDirectory(dir);
        using var file = File.Create(Path.Combine(dir, name + ".png")); png.Save(file);
        Log($"ok {suffix}/{name}.png ({w}x{h})");
    }

    static void SaveElement(FrameworkElement el, string name, string suffix)
    {
        var w = Math.Max(1, (int)Math.Ceiling(el.ActualWidth)); var h = Math.Max(1, (int)Math.Ceiling(el.ActualHeight + el.Margin.Top + el.Margin.Bottom));
        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32); var bg = new DrawingVisual();
        using (var dc = bg.RenderOpen()) dc.DrawRectangle(ThemeManager.Get("Paper"), null, new Rect(0, 0, w, h));
        bitmap.Render(bg); var vb = new VisualBrush(el) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }; var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen()) dc.DrawRectangle(vb, null, new Rect(0, el.Margin.Top, el.ActualWidth, el.ActualHeight)); bitmap.Render(dv);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); var dir = Path.Combine(output, suffix); Directory.CreateDirectory(dir);
        using var file = File.Create(Path.Combine(dir, name + ".png")); png.Save(file); Log($"ok {suffix}/{name}.png ({w}x{h})");
    }
    static async Task WaitLoaders(Window window)
    {
        var loaders = window.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(f => f.FieldType == typeof(ModuleLoader)).Select(f => f.GetValue(window) as ModuleLoader).OfType<ModuleLoader>().ToList();
        // --tempos: sem esperas fixas; Devedores carrega por páginas (sem ModuleLoader).
        if (measureOnly && window is FollowUpWindow && (bool)Field(window, "debts")!) { await Until(() => Field(window, "debtPage") != null && !(bool)Field(window, "debtLoading")!, 30000); await Idle(); return; }
        if (loaders.Count == 0) { if (measureOnly) { await Idle(); return; } await Task.Delay(1200); return; }
        var sw = Stopwatch.StartNew(); await Until(() => loaders.All(l => !l.Loading && (l.Result != null || sw.ElapsedMilliseconds > 2500)), 30000);
        if (!measureOnly) await Task.Delay(150);
    }

    // --numeros (com --tempos): todos os textos e linhas das 8 abas de Relatórios em vários períodos, para comparar antes/depois (relatorios-numeros.txt).
    static async Task DumpReports(ReportsWindow w)
    {
        var lines = new List<string>(); var fields = typeof(ReportsWindow).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).OrderBy(f => f.Name).ToList();
        string Row(object item) => item is ITuple t ? string.Join(" | ", Enumerable.Range(0, t.Length).Select(i => t[i])) : string.Join(" | ", item.GetType().GetProperties().Where(p => p.GetIndexParameters().Length == 0 && p.PropertyType != typeof(CashSession)).Select(p => $"{p.Name}={p.GetValue(item)}"));
        string Snapshot()
        {
            var sb = new StringBuilder();
            foreach (var f in fields)
                switch (f.GetValue(w))
                {
                    case TextBlock t: sb.AppendLine($"{f.Name}: {t.Text} {t.ToolTip}"); break;
                    case ItemsControl list: sb.AppendLine($"{f.Name}: {list.Items.Count} linha(s)"); foreach (var item in list.Items) sb.AppendLine("  " + Row(item)); break;
                    case Panel panel: foreach (var t in Descendants(panel).OfType<TextBlock>()) sb.AppendLine($"{f.Name}: {t.Text}"); break;
                }
            return sb.ToString();
        }
        async Task Period(string label, Action select)
        {
            select(); var last = ""; var stable = 0;
            while (stable < 4) { await Task.Delay(150); await Idle(); var now = Snapshot(); stable = now == last && !now.Contains("Carregando") ? stable + 1 : 0; last = now; }
            lines.Add($"===== {label} ====="); lines.Add(last);
        }
        void Click(string handler) => Invoke(w, handler, w, new RoutedEventArgs());
        var start = (DatePicker)w.FindName("StartDateBox"); var end = (DatePicker)w.FindName("EndDateBox");
        await Period("Este mês", () => Click("ThisMonth_Click"));
        await Period("Mês passado", () => Click("LastMonth_Click"));
        await Period("Últimos 30 dias", () => Click("Last30_Click"));
        await Period("Hoje", () => Click("Today_Click"));
        await Period("Ano de 2026", () => { start.SelectedDate = new DateTime(2026, 1, 1); end.SelectedDate = new DateTime(2026, 12, 31); });
        await Period("15/09 a 05/10", () => { start.SelectedDate = new DateTime(2026, 9, 15); end.SelectedDate = new DateTime(2026, 10, 5); });
        await Period("Tudo", () => Click("AllTime_Click"));
        File.WriteAllLines(Path.Combine(output, "relatorios-numeros.txt"), lines, Encoding.UTF8);
        Console.WriteLine("Números de Relatórios em " + Path.Combine(output, "relatorios-numeros.txt"));
    }

    static async Task Until(Func<bool> condition, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (!condition() && sw.ElapsedMilliseconds < timeoutMs) await Task.Delay(25);
    }
    static async Task Idle() => await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    static string Text(Window w, string name) => (w.FindName(name) as TextBlock)?.Text ?? "";
    static object? Field(object o, string name) => o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(o);
    static object? Call(object o, string name, params object?[] args) => o.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(o, args);
    static void Invoke(object o, string name, params object?[] args) => o.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(o, args);
    static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) { yield return child; foreach (var d in Descendants(child)) yield return d; }
    }
    static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var c = VisualTreeHelper.GetChild(root, i); yield return c; foreach (var d in Visuals(c)) yield return d; }
    }
    static string Slug(string text)
    {
        var norm = new string(text.Normalize(NormalizationForm.FormD).Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant();
        return string.Join("-", System.Text.RegularExpressions.Regex.Split(norm, "[^a-z0-9]+").Where(s => s.Length > 0)).Trim('-') is { Length: > 0 } s ? s : "aba";
    }
    static int FreePort() { var l = System.Net.Sockets.TcpListener.Create(0); l.Start(); var p = ((System.Net.IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return p; }
}
