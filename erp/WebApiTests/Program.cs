using GraficaCaixa;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

static class Program
{
    const string Password = "Isolado8A!Teste123";
    static int passed;
    static void Pass(bool condition, string label) { if (!condition) throw new Exception("FAIL " + label); passed++; Console.WriteLine("PASS " + label); }
    static async Task Code(HttpResponseMessage response, int expected, string label)
    { using (response) Pass((int)response.StatusCode == expected, $"{label} HTTP {expected}; obtido {(int)response.StatusCode}: {(response.IsSuccessStatusCode ? "" : await response.Content.ReadAsStringAsync())}"); }
    static void Sql(string database, string sql)
    { using var c = new SqliteConnection($"Data Source={database};Pooling=False"); c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
    static string Schema(string file)
    { using var db = new SqliteConnection($"Data Source={file};Mode=ReadOnly;Pooling=False"); db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT group_concat(sql,';') FROM (SELECT sql FROM sqlite_master ORDER BY type,name)"; return cmd.ExecuteScalar()!.ToString()!; }
    static int Port() { var tcp = System.Net.Sockets.TcpListener.Create(0); tcp.Start(); var port = ((IPEndPoint)tcp.LocalEndpoint).Port; tcp.Stop(); return port; }
    static HttpClient Client(int port, string pin, string? key = null)
    {
        var http = new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, cert, _, _) => RemoteDataClient.CertificateMatchesPin(cert, pin) }) { BaseAddress = new Uri($"https://127.0.0.1:{port}/") };
        if (key != null) http.DefaultRequestHeaders.Add("X-Device-Key", key); return http;
    }
    // Novo lançamento pelo celular: mesmas regras do computador, sem expor custo e sem duplicar no reenvio.
    static async Task SaleCases(HttpClient device, HttpClient reader, DataStore store, Product product)
    {
        object Closed() => new { operationId = Guid.NewGuid(), items = new object[] { new { description = "Item avulso", quantity = 1, unitPrice = 10m } } };
        Pass(!(await Get(device, "api/v1/lancamentos/opcoes")).GetProperty("cashOpen").GetBoolean(), "lançamento: opções avisam caixa fechado");
        await Code(await device.PostAsJsonAsync("api/v1/lancamentos", Closed()), 422, "lançamento: caixa fechado recusado");
        var opened = new CashSession { OpeningAmount = 0, OpenedBy = "Teste" }; store.Update(d => { d.Sessions.Add(opened); return 0; });
        var dia = await Get(device, "api/v1/relatorios/dia"); Pass(dia.TryGetProperty("received", out _) && dia.TryGetProperty("methods", out _) && !dia.TryGetProperty("expenses", out _), "resumo do dia: sem despesas");
        var anoPassado = await Get(device, "api/v1/relatorios/ano-passado"); Pass(anoPassado.TryGetProperty("billed", out _) && anoPassado.TryGetProperty("currentBilled", out _), "comparação com o ano passado");
        var hoje = await Get(device, "api/v1/hoje"); Pass(hoje.ValueKind == JsonValueKind.Array && hoje.GetRawText().IndexOf("R$", StringComparison.Ordinal) < 0, "fila Hoje: lista sem valores");
        var options = await Get(device, "api/v1/lancamentos/opcoes"); Pass(options.GetProperty("cashOpen").GetBoolean() && options.GetProperty("methods").GetArrayLength() > 0 && !HasCost(options), "lançamento: opções sem custo, caixa aberto");
        var found = await Get(device, "api/v1/lancamentos/produtos?search=" + Uri.EscapeDataString(product.Description)); Pass(found.GetProperty("items").EnumerateArray().Any(x => x.GetProperty("id").GetGuid() == product.Id) && !HasCost(found), "lançamento: busca de produto sem custo");
        object Sale(Guid op, string contact, decimal amount, bool confirmed = false, string description = "") => new { operationId = op, customerName = "Cliente Celular", contact, items = new object[] { description.Length > 0 ? new { productId = (Guid?)null, description, quantity = 1, unitPrice = 10m } : new { productId = (Guid?)product.Id, description = (string?)null, quantity = 2, unitPrice = 10m } }, payments = new object[] { confirmed ? new { id = Guid.NewGuid(), paymentType = "Dinheiro", amount, excessConfirmed = true, excessKind = PaymentPolicy.CustomerExtra } : new { id = Guid.NewGuid(), paymentType = "Dinheiro", amount } } };
        await Code(await device.PostAsJsonAsync("api/v1/lancamentos", Sale(Guid.NewGuid(), "11999998888", 25)), 422, "lançamento: valor a mais sem confirmação recusado");
        await Code(await device.PostAsJsonAsync("api/v1/lancamentos", Sale(Guid.NewGuid(), "1881", 20)), 422, "lançamento: telefone incompleto recusado");
        await Code(await device.PostAsJsonAsync("api/v1/lancamentos", new { operationId = Guid.NewGuid(), items = Array.Empty<object>() }), 422, "lançamento: sem itens recusado");
        await Code(await reader.GetAsync("api/v1/lancamentos/opcoes"), 403, "lançamento: opções sem permissão");
        await Code(await reader.PostAsJsonAsync("api/v1/lancamentos", Sale(Guid.NewGuid(), "", 20)), 403, "lançamento: gravar sem permissão");
        var before = store.Load().Sessions.Sum(x => x.Sales.Count); var op = Guid.NewGuid(); var body = Sale(op, "(11) 99999-8888", 25, true);
        using (var saved = await device.PostAsJsonAsync("api/v1/lancamentos", body))
        {
            saved.EnsureSuccessStatusCode(); var result = await saved.Content.ReadFromJsonAsync<JsonElement>();
            Pass(result.GetProperty("total").GetDecimal() == 20 && result.GetProperty("received").GetDecimal() == 25 && result.GetProperty("excess").GetDecimal() == 5 && result.GetProperty("remaining").GetDecimal() == 0 && !result.GetProperty("replayed").GetBoolean(), "lançamento: gravado com valor adicional confirmado");
        }
        using (var again = await device.PostAsJsonAsync("api/v1/lancamentos", body)) { again.EnsureSuccessStatusCode(); var result = await again.Content.ReadFromJsonAsync<JsonElement>(); Pass(result.GetProperty("replayed").GetBoolean() && store.Load().Sessions.Sum(x => x.Sales.Count) == before + 1, "lançamento: reenvio não duplica"); }
        var debt = store.Load().ManualDebts.Single(x => x.Id == op);
        Pass(debt.CustomerName == "Cliente Celular" && debt.CustomerExtra == 5 && debt.Amount == 20 && debt.Items.Single().ProductId == product.Id && debt.Remaining(store.Load()) == 0, "lançamento: conta com item do cadastro e quitada");
        await Code(await device.PostAsJsonAsync("api/v1/lancamentos", Sale(op, "11999998888", 25, true)), 409, "lançamento: mesmo identificador com outro conteúdo");
        var open = Guid.NewGuid(); await Code(await device.PostAsJsonAsync("api/v1/lancamentos", new { operationId = open, customerName = "", contact = "", items = new object[] { new { description = "Item avulso", quantity = 1, unitPrice = 10m } } }), 200, "lançamento: avulso sem pagamento e sem cliente");
        var left = store.Load().ManualDebts.Single(x => x.Id == open); Pass(left.CustomerName == "Não informado" && left.Remaining(store.Load()) == 10, "lançamento sem pagamento fica em aberto");
        // Devolve o banco de teste ao estado anterior para os próximos casos.
        store.Update(d => { d.ManualDebts.RemoveAll(x => x.Id == open || x.Id == op); d.Sessions.RemoveAll(x => x.Id == opened.Id); return 0; });
    }

    static async Task<SessionTokens> Login(HttpClient http, string user = "grafica")
    {
        using var response = await http.PostAsJsonAsync("auth/login", new { User = user, Password, Role = "Administrador", UserName = "Falso" });
        Pass(response.IsSuccessStatusCode, "login HTTPS 7C " + user); var token = (await response.Content.ReadFromJsonAsync<SessionTokens>())!;
        http.DefaultRequestHeaders.Authorization = new("Bearer", token.AccessToken); return token;
    }
    static async Task<JsonElement> Get(HttpClient http, string path)
    {
        using var response = await http.GetAsync(path); Pass(response.IsSuccessStatusCode, "GET " + path);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }
    static Task RenderFiles(ScheduledOrder order, string pdf, string imagePath)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                ReportWriter.WriteOrderSheet([order], pdf);
                var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(1, 1, 96, 96, System.Windows.Media.PixelFormats.Rgb24, null, new byte[] { 200, 100, 50 }, 3);
                var jpeg = new System.Windows.Media.Imaging.JpegBitmapEncoder(); jpeg.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using var output = File.Create(imagePath); jpeg.Save(output); done.SetResult();
            }
            catch (Exception ex) { done.SetException(ex); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return done.Task;
    }
    static bool HasPrivate(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object) return value.EnumerateObject().Any(x =>
            new[] { "hash", "salt", "password", "accessToken", "refreshToken", "photoPath", "documentsRoot", "sharedFolder", "snapshotJson" }.Contains(x.Name) || HasPrivate(x.Value));
        return value.ValueKind == JsonValueKind.Array && value.EnumerateArray().Any(HasPrivate);
    }
    static bool HasCost(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object) return value.EnumerateObject().Any(x => x.Name.Contains("cost", StringComparison.OrdinalIgnoreCase) || HasCost(x.Value));
        return value.ValueKind == JsonValueKind.Array && value.EnumerateArray().Any(HasCost);
    }
    public static async Task<int> Main(string[] args)
    {
        try { if(args.Length<2){Console.WriteLine("Uso: WebApiTests <pasta-de-teste-vazia> <caminho-do-ServerHost.exe> [snapshot.db]");return 2;}await Run(Path.GetFullPath(args[0]), Path.GetFullPath(args[1]), args.Length > 2 ? Path.GetFullPath(args[2]) : null); Console.WriteLine($"PHASE8A PASSED: {passed}"); return 0; }
        catch (Exception ex) { Console.WriteLine(ex); return 1; }
    }
    static async Task Run(string folder, string hostPath, string? snapshot)
    {
        Directory.CreateDirectory(folder); var store = new DataStore(Path.Combine(folder, "Dados")); store.EnsureInitialized();
        var customer = new CustomerProfile { Name = "Cliente Árvore", Contact = "11999990000", CpfCnpj = "PRIVADO", Address = "ENDEREÇO PRIVADO", Email = "cliente@example.invalid" };
        var product = new Product { Description = "Cartão Árvore", Category = "Papel", Price = 20, Cost = 5, ExactQuantityPrices = [new() { Quantity = 10, TotalPrice = 200, TotalCost = 50 }], QuantityPriceTiers = [new() { From = 1, UnitPrice = 20, UnitCost = 5 }] };
        product.PhotoPath = $"Produtos/Imagens/{product.Id:N}_{Guid.NewGuid():N}.jpg";
        var order = new ScheduledOrder { Number = 1, CustomerName = customer.Name, Contact = customer.Contact, Description = "Cartões", OriginalTotal = 200, OrderTotal = 200, Stage = "Agendado", Notes = "Inicial", Items = [new() { ProductId = product.Id, Product = product.Description, Quantity = 10, UnitPrice = 20, UnitCost = 5, CostReviewed = true }] };
        var delivered = new ScheduledOrder { Number = 2, CustomerName = "Devedor", Description = "Pendente", Stage = "Entregue", OrderTotal = 100, OriginalTotal = 100, PayAfterDelivery = true };
        var lossOrder = new ScheduledOrder { Number = 3, CustomerName = "Perda", Stage = "Entregue", OrderTotal = 300, OriginalTotal = 300, PayAfterDelivery = true };
        var undefined = new ScheduledOrder { Number = 4, CustomerName = "A definir", Stage = "Entregue", PricePending = true, PayAfterDelivery = true };
        var cash = new CashSession { OpeningAmount = 0, OpenedBy = "Teste", ClosedAt = DateTime.Now, ClosedBy = "Teste", DeclaredClosingAmount = 0 };
        var sale = new Sale { CustomerName = "Avulso", Product = "Amortização", TotalValue = 25, PaymentType = "PIX" }; cash.Sales.Add(sale);
        var debt = new ManualDebt { CustomerName = "Avulso", Amount = 80, SaleIds = [sale.Id] };
        var fiscal = new FiscalDocument { OrderId = order.Id, PdfPath = "DocumentosFiscais/fiscal-teste.pdf" };
        store.Update(d =>
        {
            d.Prompt12.LoginPassword = LoginSecurity.Create(Password); d.Prompt12.OwnerPin = OwnerSecurity.Create("918273");
            d.Customers = [customer]; d.Products = [product]; d.ScheduledOrders = [order, delivered, lossOrder, undefined]; d.Sessions = [cash]; d.ManualDebts = [debt];
            d.Prompt12.Losses.Add(new() { OrderId = lossOrder.Id, Amount = 300, Status = LossStatus.Loss });
            d.Prompt12.FiscalDocuments.Add(fiscal);
            for (int i = 0; i < 120; i++) { d.Customers.Add(new() { Name = $"Cliente {i:000}" }); d.Products.Add(new() { Description = $"Produto {i:000}" }); d.ScheduledOrders.Add(new() { Number = 5 + i, CustomerName = $"Busca {i:000}", Stage = "Agendado", FullyPaid = true }); }
            return 0;
        });
        var security = new IdentitySecurity(store); security.Migrate(); var beforeSchema = Schema(store.DatabasePath);
        var documents = Path.Combine(folder, "Arquivos"); Directory.CreateDirectory(Path.Combine(documents, "Impressões")); Directory.CreateDirectory(Path.Combine(documents, "Produtos", "Imagens"));
        var pdf = Path.Combine(documents, "Impressões", "pedido-teste.pdf");
        await RenderFiles(order, pdf, Path.Combine(documents, product.PhotoPath.Replace('/', Path.DirectorySeparatorChar)));
        foreach (var name in new[] { "Recibos", "Orcamentos", "Fechamentos", "DocumentosFiscais" }) Directory.CreateDirectory(Path.Combine(documents, name));
        File.Copy(pdf, Path.Combine(documents, "Recibos", "recibo-teste.pdf")); File.Copy(pdf, Path.Combine(documents, "Orcamentos", "orcamento-teste.pdf")); File.Copy(pdf, Path.Combine(documents, "DocumentosFiscais", "fiscal-teste.pdf"));
        File.Copy(pdf, Path.Combine(documents, "Fechamentos", $"Fechamento_{cash.OpenedAt:dd_MM_yyyy_HH'h'mm}_Original_v1.pdf"));
        var options = new WebApiOptions { DocumentsDirectory = documents, AllowedOrigins = ["https://pwa.example.invalid"], Documents = [new("pedido", order.Id, "Impressões/pedido-teste.pdf"), new("pedido", delivered.Id, "Impressões/ausente.pdf"), new("recibo", order.Id, "Recibos/recibo-teste.pdf"), new("orcamento", order.Id, "Orcamentos/orcamento-teste.pdf")] };
        var port = Port();
        await using var server = await SharedDataServer.StartAsync(store, Path.Combine(folder, "Identidade"), port, Path.Combine(folder, "Logs"), webOptions: options);
        using var anonymous = Client(port, server.Connection.CertificatePin); using var device = Client(port, server.Connection.CertificatePin, server.Connection.Key);
        var paths = new[] { "api/v1/dashboard", "api/v1/pedidos", $"api/v1/pedidos/{order.Id}", "api/v1/clientes", $"api/v1/clientes/{customer.Id}", "api/v1/produtos", $"api/v1/produtos/{product.Id}", "api/v1/devedores", $"api/v1/devedores/{debt.Id}", $"api/v1/produtos/{product.Id}/imagem", $"api/v1/documentos/pedido/{order.Id}" };
        foreach (var path in paths) { await Code(await anonymous.GetAsync(path), 401, "sem dispositivo " + path); await Code(await device.GetAsync(path), 401, "sem sessão " + path); }
        device.DefaultRequestHeaders.Authorization = new("Bearer", new string('a', 64)); await Code(await device.GetAsync(paths[0]), 401, "sessão inválida");
        var owner = await Login(device);
        foreach (var path in paths.Take(9)) { var data = await Get(device, path); Pass(!HasPrivate(data), "DTO sem segredos/modelos internos " + path); }
        var paging = await Get(device, "api/v1/pedidos?page=1&pageSize=2"); Pass(paging.GetProperty("items").GetArrayLength() == 2 && paging.GetProperty("total").GetInt64() == 124, "paginação SQL");
        var next = await Get(device, "api/v1/pedidos?page=2&pageSize=2"); Pass(paging.GetProperty("items")[0].GetProperty("id").GetGuid() != next.GetProperty("items")[0].GetProperty("id").GetGuid(), "páginas distintas");
        foreach (var list in new[] { "pedidos", "clientes", "produtos", "devedores" })
        {
            var maximum = await Get(device, $"api/v1/{list}?pageSize=100000"); Pass(maximum.GetProperty("pageSize").GetInt32() == 100 && maximum.GetProperty("items").GetArrayLength() <= 100, "limite seguro " + list);
            var empty = await Get(device, $"api/v1/{list}?page=2147483647"); Pass(empty.GetProperty("items").GetArrayLength() == 0, "página vazia/overflow " + list);
            var search = await Get(device, $"api/v1/{list}?search=INEXISTENTE"); Pass(search.GetProperty("total").GetInt32() == 0, "busca vazia " + list);
            await Code(await device.GetAsync($"api/v1/{list}?pageSize=0"), 400, "paginação inválida " + list);
        }
        Pass((await Get(device, "api/v1/pedidos?search=Busca%20005")).GetProperty("total").GetInt32() == 1, "busca pedido");
        Pass((await Get(device, "api/v1/clientes?search=Cliente%20005")).GetProperty("total").GetInt32() == 1, "busca cliente");
        Pass((await Get(device, "api/v1/produtos?search=Produto%20005")).GetProperty("total").GetInt32() == 1, "busca produto");
        await Code(await device.GetAsync("api/v1/pedidos?from=2026-13-01"), 400, "data inválida"); await Code(await device.GetAsync("api/v1/pedidos?stage=FALSA"), 400, "etapa inválida");
        Pass((await Get(device, "api/v1/pedidos?stage=Entregue")).GetProperty("total").GetInt32() == 3, "filtro por etapa");
        var debts = await Get(device, "api/v1/devedores"); Pass(debts.GetProperty("total").GetInt32() == 3, "dívidas excluem perdas e preservam indefinido");
        Pass(debts.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == debt.Id).GetProperty("remaining").GetDecimal() == 55, "saldo avulso usa recebimentos existentes");
        var dashboard = await Get(device, "api/v1/dashboard"); Pass(dashboard.GetProperty("receivable").GetDecimal() == 155 && dashboard.GetProperty("undefinedDebts").GetInt32() == 1, "dashboard usa regras de saldo e perda");
        using (var image = await device.GetAsync(paths[9])) Pass(image.IsSuccessStatusCode && image.Content.Headers.ContentType!.MediaType == "image/jpeg" && image.Headers.CacheControl!.NoStore && image.Headers.GetValues("X-Content-Type-Options").Single() == "nosniff", "foto MIME/no-store/nosniff");
        using (var document = await device.GetAsync(paths[10])) Pass(document.IsSuccessStatusCode && document.Content.Headers.ContentType!.MediaType == "application/pdf" && Encoding.ASCII.GetString(await document.Content.ReadAsByteArrayAsync()).StartsWith("%PDF-"), "PDF HTTP autenticado");
        foreach (var path in new[] { $"api/v1/documentos/recibo/{order.Id}", $"api/v1/documentos/orcamento/{order.Id}", $"api/v1/documentos/fechamento/{cash.Id}" }) await Code(await device.GetAsync(path), 200, "tipo de documento " + path);
        await Code(await device.GetAsync($"api/v1/documentos/fiscal/{fiscal.Id}"), 403, "fiscal privado exige sessão owner");
        await Code(await device.PostAsJsonAsync("auth/owner", new OwnerPinRequest("918273", false)), 200, "owner existente validado");
        await Code(await device.GetAsync($"api/v1/documentos/fiscal/{fiscal.Id}"), 200, "fiscal com vínculo persistido seguro");
        store.Update(d => { d.Prompt12.FiscalDocuments.Single(x => x.Id == fiscal.Id).PdfPath = @"C:\Windows\secret.pdf"; return 0; });
        await Code(await device.GetAsync($"api/v1/documentos/fiscal/{fiscal.Id}"), 403, "caminho fiscal arbitrário bloqueado");
        store.Update(d => { d.Prompt12.FiscalDocuments.Single(x => x.Id == fiscal.Id).PdfPath = fiscal.PdfPath; return 0; });
        await Code(await device.GetAsync($"api/v1/documentos/pedido/{delivered.Id}"), 404, "PDF ausente");
        await Code(await device.GetAsync($"api/v1/documentos/pedido/{Guid.NewGuid()}"), 404, "entidade inexistente");
        await Code(await device.GetAsync($"api/v1/produtos/{Guid.NewGuid()}/imagem"), 404, "foto inexistente");
        await Code(await device.GetAsync(paths[9] + "?path=C:/Windows/secret.jpg"), 400, "caminho não é parâmetro de foto");
        await Code(await device.GetAsync(paths[10] + "?path=../Dados/caixa_dados.db"), 400, "caminho não é parâmetro de documento");
        foreach (var bad in new[] { "../segredo.pdf", "..\\segredo.pdf", "C:/segredo.pdf", "//PC1/ERP/x.pdf", "file://x.pdf", "Impressões/../x.pdf", "Impressões/x.pdf:secret", "Impressões/%2e%2e/x.pdf", "Impressões/x.pdf.", "Impressões/a?/x.pdf" }) Pass(!WebFiles.ValidRelative(bad, "Impressões"), "traversal/absoluto/ADS bloqueado " + bad);
        var originalPhoto = product.PhotoPath;
        foreach (var bad in new[] { "../secret.jpg", @"C:\secret.jpg", @"\\PC1\ERP\secret.jpg", "file:///secret.jpg", $"Produtos/Imagens/{Guid.NewGuid():N}_{Guid.NewGuid():N}.jpg" })
        {
            store.Update(d => { d.Products.Single(x => x.Id == product.Id).PhotoPath = bad; return 0; });
            await Code(await device.GetAsync(paths[9]), 403, "foto manipulada recusada");
        }
        store.Update(d => { d.Products.Single(x => x.Id == product.Id).PhotoPath = originalPhoto; return 0; });
        var external = Path.Combine(folder, "ForaDaRaiz"); Directory.CreateDirectory(external); await File.WriteAllTextAsync(Path.Combine(external, "secret.pdf"), "%PDF-segredo");
        var junction = Path.Combine(documents, "Impressões", "junction");
        var junctionScript = Path.Combine(folder, "junction.ps1"); await File.WriteAllTextAsync(junctionScript, "New-Item -ItemType Junction -Path $args[0] -Target $args[1] -ErrorAction Stop | Out-Null");
        var junctionStart = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", junctionScript, junction, external }) junctionStart.ArgumentList.Add(argument);
        using (var creation = Process.Start(junctionStart)!) { var outTask = creation.StandardOutput.ReadToEndAsync(); var errTask = creation.StandardError.ReadToEndAsync(); await creation.WaitForExitAsync(); Pass(creation.ExitCode == 0, "fixture junction real criada: " + await outTask + await errTask); }
        var originalBinding = options.Documents[0]; options.Documents[0] = new("pedido", order.Id, "Impressões/junction/secret.pdf");
        await Code(await device.GetAsync(paths[10]), 403, "junction para fora da raiz bloqueada por HTTP"); options.Documents[0] = originalBinding;
        var imagePath = Path.Combine(documents, originalPhoto.Replace('/', Path.DirectorySeparatorChar)); var goodImage = await File.ReadAllBytesAsync(imagePath);
        await File.WriteAllTextAsync(imagePath, "Conteúdo não JPEG"); await Code(await device.GetAsync(paths[9]), 422, "imagem com conteúdo inválido recusada"); await File.WriteAllBytesAsync(imagePath, goodImage);
        var goodPdf = await File.ReadAllBytesAsync(pdf); await File.WriteAllTextAsync(pdf, "Conteúdo não PDF"); await Code(await device.GetAsync(paths[10]), 422, "PDF com conteúdo inválido recusado"); await File.WriteAllBytesAsync(pdf, goodPdf);
        foreach (var bad in new[] { "Impressões/CON.pdf", "Impressões/NUL", "Impressões/LPT1.pdf" }) Pass(!WebFiles.ValidRelative(bad, "Impressões"), "dispositivo Windows não é documento " + bad);
        await Code(await device.PostAsJsonAsync("auth/users", new UserEdit(null, "Leitor", "leitor", "Financeiro", true, Password)), 200, "usuário restrito existente");
        using var reader = Client(port, server.Connection.CertificatePin, server.Connection.Key); var employee = await Login(reader, "leitor");
        security.SavePermissions(owner.User, new(employee.User.UserId, null, new() { ["costs.edit"] = false, ["orders.value"] = false, ["finance.receive"] = false, ["customers.edit"] = false, ["orders.stage"] = false }), "8A teste");
        foreach (var path in paths.Take(9)) { var data = await Get(reader, path); Pass(!HasCost(data) && !HasPrivate(data), "privados/custos ausentes no JSON " + path); }
        Pass(!(await Get(reader, $"api/v1/clientes/{customer.Id}")).TryGetProperty("cpfCnpj", out _), "CPF ausente sem permissão");
        Pass(!(await Get(reader, $"api/v1/pedidos/{order.Id}")).TryGetProperty("production", out _), "produção filtrada por permissão");
        await Code(await reader.GetAsync(paths[10]), 403, "PDF recusado sem permissão privada");
        var partial = await Get(reader, "api/v1/dashboard"); Pass(partial.TryGetProperty("receivable", out _), "devedores autorizados visíveis sem custos");
        foreach (var (permission, path) in new[] { ("view.orders", paths[1]), ("view.customers", paths[3]), ("view.products", paths[5]), ("view.debtors", paths[7]), ("view.home", paths[0]) })
        { security.SavePermissions(owner.User, new(employee.User.UserId, null, new() { [permission] = false }), "8A teste"); await Code(await reader.GetAsync(path), 403, "permissão negada " + permission); }
        security.SavePermissions(owner.User, new(employee.User.UserId, null, new() { ["view.home"] = true }), "8A teste");
        var hiddenDashboard = await Get(reader, "api/v1/dashboard"); Pass(!hiddenDashboard.TryGetProperty("receivable", out _) && !hiddenDashboard.TryGetProperty("openOrders", out _), "dashboard omite indicadores sem permissão");
        await Code(await reader.GetAsync(paths[9]), 403, "foto sem autorização");
        var filteredDashboard = await Get(device, "api/v1/dashboard"); Pass(filteredDashboard.GetProperty("pendingDebts").GetInt32() == 3, "indicadores autorizados");
        var command = new WebNoteCommand(Guid.NewGuid(), order.Revision, "Observação pelo comando específico");
        await Code(await device.PatchAsJsonAsync($"api/v1/pedidos/{order.Id}/observacao", command), 200, "revisão válida");
        var retry = await device.PatchAsJsonAsync($"api/v1/pedidos/{order.Id}/observacao", command); using (retry) Pass(retry.IsSuccessStatusCode && (await retry.Content.ReadFromJsonAsync<WebCommandResult>())!.Replayed, "retry idempotente");
        await Code(await device.PatchAsJsonAsync($"api/v1/pedidos/{order.Id}/observacao", command with { OperationId = Guid.NewGuid() }), 409, "revisão antiga");
        await Code(await device.PatchAsJsonAsync($"api/v1/pedidos/{order.Id}/observacao", command with { Observacao = "Alterada" }), 409, "mesmo id/outro payload");
        var current = store.Load(); var updated = current.ScheduledOrders.Single(x => x.Id == order.Id);
        Pass(updated.Revision == 1 && updated.Notes == command.Observacao && current.AuditTrail.Count(x => x.EntityId == order.Id.ToString() && x.UserId == owner.User.UserId.ToString() && x.SessionId == owner.User.SessionId.ToString()) == 1, "uma escrita e auditoria de identidade real");
        var stage = new WebStageCommand(Guid.NewGuid(), 1, "Produção"); await Code(await device.PatchAsJsonAsync($"api/v1/pedidos/{order.Id}/etapa", stage), 200, "etapa reutiliza regra existente");
        await Code(await device.PatchAsJsonAsync($"api/v1/pedidos/{order.Id}/etapa", stage), 200, "retry de etapa sem duplicar revisão");
        updated = store.Load().ScheduledOrders.Single(x => x.Id == order.Id); Pass(updated.Revision == 2 && updated.ArtworkApprovedAt != null, "prazo atualizado pela regra de aprovação");
        await Code(await device.PatchAsJsonAsync($"api/v1/pedidos/{order.Id}/etapa", stage with { OperationId = Guid.NewGuid(), ExpectedRevision = 2, Etapa = "Entregue" }), 422, "entrega financeira indisponível");
        var writes = new[] { new WebNoteCommand(Guid.NewGuid(), 2, "Concorrente A"), new WebNoteCommand(Guid.NewGuid(), 2, "Concorrente B") };
        var racing = await Task.WhenAll(writes.Select(x => device.PatchAsJsonAsync($"api/v1/pedidos/{order.Id}/observacao", x)));
        Pass(racing.Select(x => (int)x.StatusCode).Order().SequenceEqual(new[] { 200, 409 }), "duas escritas concorrentes não sobrescrevem"); foreach (var r in racing) r.Dispose();
        await Code(await device.PatchAsJsonAsync($"api/v1/pedidos/{order.Id}/observacao", new { OperationId = Guid.NewGuid(), ExpectedRevision = 3, Observacao = "X", Role = "Administrador" }), 400, "identidade extra no comando recusada");
        await Code(await device.PatchAsJsonAsync($"api/v1/pedidos/{order.Id}/observacao", new { OperationId = Guid.NewGuid(), Observacao = "X" }), 400, "revisão obrigatória");
        await Code(await device.PatchAsJsonAsync($"api/v1/pedidos/{order.Id}/observacao", new { OperationId = Guid.NewGuid(), ExpectedRevision = 3, Observacao = new string('x', 9000) }), 413, "corpo limitado");
        using (var request = new HttpRequestMessage(HttpMethod.Options, paths[1]))
        {
            request.Headers.Add("Origin", "https://pwa.example.invalid"); request.Headers.Add("Access-Control-Request-Method", "GET"); request.Headers.Add("Access-Control-Request-Headers", "authorization,x-device-key");
            using var result = await anonymous.SendAsync(request); Pass(result.IsSuccessStatusCode && result.Headers.GetValues("Access-Control-Allow-Origin").Single() == "https://pwa.example.invalid" && !result.Headers.Contains("Access-Control-Allow-Credentials"), "preflight restrito sem credenciais");
        }
        using (var request = new HttpRequestMessage(HttpMethod.Options, paths[1]))
        { request.Headers.Add("Origin", "https://evil.example.invalid"); request.Headers.Add("Access-Control-Request-Method", "GET"); using var result = await anonymous.SendAsync(request); Pass(!result.Headers.Contains("Access-Control-Allow-Origin"), "origem não cadastrada bloqueada"); }
        using (var request = new HttpRequestMessage(HttpMethod.Options, "auth/login"))
        { request.Headers.Add("Origin", "https://pwa.example.invalid"); request.Headers.Add("Access-Control-Request-Method", "POST"); using var result = await anonymous.SendAsync(request); Pass(result.IsSuccessStatusCode && result.Headers.GetValues("Access-Control-Allow-Origin").Single() == "https://pwa.example.invalid", "CORS permite reutilizar login 7C"); }
        var countsBeforeCancel = store.Load().ScheduledOrders.Single(x => x.Id == order.Id).Revision;
        using (var canceled = new CancellationTokenSource())
        { canceled.Cancel(); try { await store.WebCommandAsync(owner.User, order.Id, Guid.NewGuid(), countsBeforeCancel, "Cancelada", false, canceled.Token); throw new Exception("Cancelamento esperado"); } catch (OperationCanceledException) { Pass(store.Load().ScheduledOrders.Single(x => x.Id == order.Id).Revision == countsBeforeCancel, "comando cancelado não persiste"); } }
        await Code(await reader.PostAsync("auth/logout", null), 200, "logout"); await Code(await reader.GetAsync(paths[0]), 401, "sessão revogada");
        employee = await Login(reader, "leitor"); security.SavePermissions(owner.User, new(employee.User.UserId, null, new() { ["finance.receive"] = false }), "8A teste");
        await SaleCases(device, reader, store, product);
        employee = await Login(reader, "leitor"); security.SaveUser(owner.User, new(employee.User.UserId, "Leitor", "leitor", "Financeiro", false, null), "8A teste"); await Code(await reader.GetAsync(paths[0]), 401, "usuário inativo");
        using (var foreign = Client(port, server.Connection.CertificatePin, security.BootstrapDevice("Outro", "teste").Secret))
        { foreign.DefaultRequestHeaders.Authorization = new("Bearer", owner.AccessToken); await Code(await foreign.GetAsync(paths[1]), 401, "sessão de outro dispositivo"); }
        using (var legacy = await device.GetAsync("data")) Pass(legacy.IsSuccessStatusCode && (await legacy.Content.ReadFromJsonAsync<AppData>())!.ScheduledOrders.Count == 124, "/data WPF preservado");
        string productMetadata;
        using (var db = new SqliteConnection($"Data Source={store.DatabasePath};Pooling=False"))
        { db.Open(); productMetadata = WebQueries.Metadata(db, "products_v1"); }
        Sql(store.DatabasePath, "UPDATE metadata SET value='{' WHERE key='products_v1'");
        using (var failure = await device.GetAsync("api/v1/produtos"))
        { var text = await failure.Content.ReadAsStringAsync(); Pass((int)failure.StatusCode == 500 && !text.Contains("JsonException") && !text.Contains("System.") && !text.Contains(store.DatabasePath), "falha inesperada 500 sem stack trace/caminho"); }
        Sql(store.DatabasePath, "UPDATE metadata SET value='" + productMetadata.Replace("'", "''") + "' WHERE key='products_v1'");
        Pass(Schema(store.DatabasePath) == beforeSchema, "schema inalterado pela API V1");
        await Native(folder, hostPath, store, security, options);
        if (snapshot != null) await Performance(folder, snapshot);
    }
    static async Task Native(string folder, string hostPath, DataStore store, IdentitySecurity security, WebApiOptions options)
    {
        var port = Port(); Pass(port != 5074, "porta ServerHost isolada");
        var configDir = Path.Combine(folder, "Configuracao"); Directory.CreateDirectory(configDir); var config = Path.Combine(configDir, "servidor.json");
        await File.WriteAllTextAsync(config, JsonSerializer.Serialize(new ServerConfiguration { DataDirectory = store.Folder, IdentityDirectory = Path.Combine(folder, "Identidade"), LogsDirectory = Path.Combine(folder, "LogsNative"), Port = port, WebApi = options }));
        using var identity = ServerIdentityStore.LoadExisting(Path.Combine(folder, "Identidade")); var credential = security.BootstrapDevice("ServerHost Teste", "8A isolado");
        using var http = Client(port, identity.CertificatePin, credential.Secret);
        var start = new ProcessStartInfo(hostPath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--configuration"); start.ArgumentList.Add(config);
        using var process = Process.Start(start)!; var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        try
        {
            bool ready = false; for (int attempt = 0; attempt < 100 && !process.HasExited; attempt++)
            { try { using var health = await http.GetAsync("health"); if (health.IsSuccessStatusCode) { ready = true; break; } } catch (HttpRequestException) { } await Task.Delay(100); }
            Pass(ready, "ServerHost executável HTTPS pronto"); await Login(http);
            var entity = store.Load(); var order = entity.ScheduledOrders.First(); var product = entity.Products.First(); var customer = entity.Customers.First(); var debt = entity.ManualDebts.First();
            foreach (var path in new[] { "api/v1/dashboard", "api/v1/pedidos", $"api/v1/pedidos/{order.Id}", "api/v1/clientes", $"api/v1/clientes/{customer.Id}", "api/v1/produtos", $"api/v1/produtos/{product.Id}", $"api/v1/produtos/{product.Id}/imagem", "api/v1/devedores", $"api/v1/devedores/{debt.Id}", $"api/v1/documentos/pedido/{order.Id}" }) await Code(await http.GetAsync(path), 200, "ServerHost real " + path);
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); await File.WriteAllTextAsync(Path.Combine(folder, "serverhost.log"), await output + await error); }
    }
    static async Task Performance(string folder, string snapshot)
    {
        var root = Path.Combine(folder, "Performance"); var dataDir = Path.Combine(root, "Dados"); Directory.CreateDirectory(dataDir); File.Copy(snapshot, Path.Combine(dataDir, "caixa_dados.db"));
        var store = new DataStore(dataDir, true); store.EnsureInitialized(); var security = new IdentitySecurity(store); security.Migrate();
        var credential = LoginSecurity.Create(Password);
        using (var db = new SqliteConnection($"Data Source={store.DatabasePath};Pooling=False"))
        { db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "UPDATE auth_users SET password_hash=@hash,salt=@salt,iterations=@iterations,must_change=0 WHERE role='Administrador'"; cmd.Parameters.AddWithValue("@hash", credential.Hash); cmd.Parameters.AddWithValue("@salt", credential.Salt); cmd.Parameters.AddWithValue("@iterations", credential.Iterations); cmd.ExecuteNonQuery(); }
        var data = store.Load(); var port = Port(); await using var server = await SharedDataServer.StartAsync(store, Path.Combine(root, "Identidade"), port);
        using var http = Client(port, server.Connection.CertificatePin, server.Connection.Key); await Login(http);
        var report = new List<object>();
        foreach (var path in new[] { "data", "api/v1/dashboard", "api/v1/pedidos?pageSize=50", "api/v1/clientes?pageSize=50", "api/v1/produtos?pageSize=50", "api/v1/devedores?pageSize=50" })
        {
            using (var warmup = await http.GetAsync(path)) warmup.EnsureSuccessStatusCode();
            var timings = new List<double>(); int bytes = 0, records = 0; long? total = null;
            for (int i = 0; i < 5; i++)
            {
                var watch = Stopwatch.StartNew(); using var response = await http.GetAsync(path); response.EnsureSuccessStatusCode(); var body = await response.Content.ReadAsByteArrayAsync(); watch.Stop();
                timings.Add(watch.Elapsed.TotalMilliseconds); bytes = body.Length; using var json = JsonDocument.Parse(body);
                if (json.RootElement.TryGetProperty("items", out var items)) { records = items.GetArrayLength(); total = json.RootElement.GetProperty("total").GetInt64(); }
                else if (path == "data") records = data.ScheduledOrders.Count + data.Products.Count + data.Customers.Count + data.Sessions.Sum(x => x.Sales.Count) + data.AuditTrail.Count;
            }
            report.Add(new { Endpoint = path, Bytes = bytes, Records = records, Total = total, MedianMs = Math.Round(timings.Order().ElementAt(2), 2), MinMs = Math.Round(timings.Min(), 2), MaxMs = Math.Round(timings.Max(), 2) });
        }
        await File.WriteAllTextAsync(Path.Combine(folder, "performance.json"), JsonSerializer.Serialize(new { Source = "Snapshot de produção preservado na Fase 7C, migrado somente nesta cópia", Counts = new { Orders = data.ScheduledOrders.Count, Customers = data.Customers.Count, Products = data.Products.Count, Sales = data.Sessions.Sum(x => x.Sales.Count), Audit = data.AuditTrail.Count }, Measurements = report }, new JsonSerializerOptions { WriteIndented = true }));
        Pass(report.Count == 6, "performance real HTTPS versus /data; cinco amostras aquecidas");
    }
}
