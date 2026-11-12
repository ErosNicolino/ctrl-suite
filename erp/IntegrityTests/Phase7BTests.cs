using GraficaCaixa;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
static class Phase7BTests
{
    static int count;const string Password="Fase7B-Teste!123";
    static void Pass(bool valid,string label){if(!valid)throw new Exception("FAIL "+label);count++;Console.WriteLine("PASS "+label);}
    static async Task Expect(HttpResponseMessage response,HttpStatusCode expected,string label){using(response){Pass(response.StatusCode==expected,label+" ["+(int)response.StatusCode+"] "+(response.StatusCode==expected?"":await response.Content.ReadAsStringAsync()));}}
    public static void Run(string folder)=>RunAsync(folder).GetAwaiter().GetResult();
    static async Task RunAsync(string folder)
    {
        Directory.CreateDirectory(folder);var store=new DataStore(Path.Combine(folder,"data"));store.EnsureInitialized();store.Update(d=>{d.Prompt12.LoginPassword=LoginSecurity.Create(Password);d.Prompt12.OwnerPin=OwnerSecurity.Create("912345");return 0;});
        var security=new IdentitySecurity(store);security.Migrate();security.Migrate();
        var port=System.Net.Sockets.TcpListener.Create(0);port.Start();int number=((System.Net.IPEndPoint)port.LocalEndpoint).Port;port.Stop();
        await using var server=await SharedDataServer.StartAsync(store,Path.Combine(folder,"identity"),number,Path.Combine(folder,"logs"));var connection=server.Connection with {Url=$"https://127.0.0.1:{number}",Urls=null};
        HttpClient Client(){var handler=new HttpClientHandler{ServerCertificateCustomValidationCallback=(_,cert,_,_)=>RemoteDataClient.CertificateMatchesPin(cert,connection.CertificatePin)};var http=new HttpClient(handler){BaseAddress=new Uri(connection.Url+"/")};http.DefaultRequestHeaders.Add("X-Device-Key",connection.Key);return http;}
        using var admin=Client();using var employee=Client();
        async Task<SessionTokens> Login(HttpClient http,string name,string pass=Password){using var response=await http.PostAsJsonAsync("auth/login",new{User=name,Password=pass,Role="Administrador",UserId=Guid.NewGuid(),Permission=true});Pass(response.IsSuccessStatusCode,"login HTTPS "+name);var tokens=(await response.Content.ReadFromJsonAsync<SessionTokens>())!;http.DefaultRequestHeaders.Authorization=new("Bearer",tokens.AccessToken);return tokens;}
        var owner=await Login(admin,"grafica");
        async Task<AppData> Data(HttpClient http)=> (await http.GetFromJsonAsync<AppData>("data"))!;
        async Task<CommitRequest> Draft(HttpClient http,Action<AppData> change){var d=await Data(http);var version=d.DataVersion;change(d);return new(Guid.NewGuid(),version,d,8);}
        async Task Change(HttpClient http,Action<AppData> change,string label,HttpStatusCode expected=HttpStatusCode.OK){var request=await Draft(http,change);await Expect(await http.PostAsJsonAsync("commit",request),expected,label);}
        async Task Rules(Guid? id,string? role,Dictionary<string,bool?> values){await Expect(await admin.PostAsJsonAsync("auth/permissions",new PermissionEdit(id,role,values)),HttpStatusCode.OK,"perfil / exceções configurados");}
        await Expect(await admin.PostAsJsonAsync("auth/users",new UserEdit(null,"Maria Teste","maria","Financeiro",true,Password)),HttpStatusCode.OK,"criar usuário");var users=(await admin.GetFromJsonAsync<List<UserSummary>>("auth/users"))!;var maria=users.Single(u=>u.Login=="maria");
        await Expect(await admin.PostAsJsonAsync("auth/users",new UserEdit(null,"Duplicado","MARIA","Financeiro",true,Password)),HttpStatusCode.Conflict,"login duplicado normalizado");
        var employeeTokens=await Login(employee,"maria");Pass(employeeTokens.User.Role=="Financeiro"&&employeeTokens.User.UserId==maria.Id,"role / userId do payload não alteram identidade");
        await Expect(await employee.GetAsync("auth/users"),HttpStatusCode.Forbidden,"painel administrativo negado");await Expect(await employee.GetAsync("audit"),HttpStatusCode.Forbidden,"auditoria central negada");await Expect(await employee.PostAsJsonAsync("auth/permissions",new PermissionEdit(maria.Id,null,new(){{"system.users",true}})),HttpStatusCode.Forbidden,"autopromoção recusada");
        await Expect(await admin.PostAsJsonAsync("auth/permissions",new PermissionEdit(maria.Id,null,new(){{"inventada",true}})),HttpStatusCode.BadRequest,"permissão inexistente recusada");
        using(var anonymous=Client()){await Expect(await anonymous.GetAsync("auth/me"),HttpStatusCode.Unauthorized,"sessão ausente 401");anonymous.DefaultRequestHeaders.Authorization=new("Bearer",new string('0',64));await Expect(await anonymous.PostAsJsonAsync("commit",new{OperationId=Guid.NewGuid(),UserId=owner.User.UserId,Role="Administrador",Permission=true}),HttpStatusCode.Unauthorized,"token inválido 401");anonymous.DefaultRequestHeaders.Authorization=new("Bearer",employeeTokens.AccessToken[..63]+(employeeTokens.AccessToken[63]=='A'?'B':'A'));await Expect(await anonymous.GetAsync("data"),HttpStatusCode.Unauthorized,"token adulterado 401");}
        await Change(admin,d=>d.Sessions.Add(new CashSession{OpeningAmount=100,OpenedBy="Falsificado"}),"abrir caixa no servidor");var cash=(await Data(admin)).Sessions.Single(s=>s.IsOpen);Pass(cash.OpenedBy==owner.User.Name,"autoria da abertura definida pela sessão");
        var order=new ScheduledOrder{Number=1,CustomerName="Cliente Teste",Description="Cartões",Items=[new(){Quantity=10,Product="Cartões",UnitPrice=20}],OrderTotal=200,OriginalTotal=200,Stage="Agendado",PayAfterDelivery=true};
        await Change(admin,d=>OrderOperations.Save(d,order,OperationContext.Administrator("Forjado")),"criar pedido");
        await Change(employee,d=>d.ScheduledOrders.Single().IsUncollected=true,"cancelamento sem permissão 403",HttpStatusCode.Forbidden);
        await Change(admin,d=>{var o=d.ScheduledOrders.Single();o.IsUncollected=true;o.ArchivedAsUncollectedAt=DateTime.Now;},"cancelar / arquivar preservando pedido");
        await Change(admin,d=>d.ScheduledOrders.Single().IsUncollected=false,"restaurar pedido arquivado");
        await Rules(maria.Id,null,new(){{"orders.stage",false},{"orders.value",false},{"orders.quantity",false},{"orders.deliver",false},{"finance.receive",false}});
        await Change(employee,d=>d.ScheduledOrders.Single().Stage="Produção","sem permissão de etapa 403",HttpStatusCode.Forbidden);
        var forged=await Draft(employee,d=>d.ScheduledOrders.Single().Stage="Produção");await Expect(await employee.PostAsJsonAsync("commit",new{forged.OperationId,forged.ExpectedVersion,forged.Data,forged.ClientOrderDetailsVersion,UserId=owner.User.UserId,Role="Administrador",Permission=true}),HttpStatusCode.Forbidden,"identidade e permissão forjadas no commit sem efeito");
        await Change(employee,d=>d.ScheduledOrders.Single().OrderTotal=250,"valor protegido 403",HttpStatusCode.Forbidden);await Change(employee,d=>d.ScheduledOrders.Single().Items[0].Quantity=50,"quantidade protegida 403",HttpStatusCode.Forbidden);
        await Rules(maria.Id,null,new(){{"orders.stage",true}});await Change(employee,d=>OrderOperations.AdvanceStage(d,order.Id,d.ScheduledOrders.Single().Revision,"Produção",OperationContext.Administrator("Forjado"),true),"exceção permite etapa imediatamente");
        await Change(employee,d=>d.ScheduledOrders.Single().Stage="Entregue","entrega protegida independente 403",HttpStatusCode.Forbidden);
        await Rules(maria.Id,null,new(){{"orders.deliver",true}});await Change(employee,d=>{var draft=JsonSerializer.Deserialize<ScheduledOrder>(JsonSerializer.Serialize(d.ScheduledOrders.Single()))!;draft.Stage="Entregue";OrderOperations.Save(d,draft,OperationContext.Administrator("Forjado"));},"entregar sem receber");Pass((await Data(admin)).ScheduledOrders.Single().PaidAmount==0,"entrega não cria pagamento");
        await Rules(maria.Id,null,new(){{"finance.receive",true}});
        await Change(employee,d=>OrderOperations.AdvanceStage(d,order.Id,d.ScheduledOrders.Single().Revision,"Produção",OperationContext.Administrator("Forjado"),true),"voltar etapa para testar recebimento independente");
        var paymentId=Guid.NewGuid();var payment=await Draft(employee,d=>OrderOperations.AddPayment(d,d.ScheduledOrders.Single(),150,"PIX",DateTime.Now,"Pagamento",paymentId,OperationContext.Administrator("Forjado")));
        await Expect(await employee.PostAsJsonAsync("commit",payment),HttpStatusCode.OK,"receber pagamento");await Expect(await employee.PostAsJsonAsync("commit",payment),HttpStatusCode.OK,"retry com mesmo identificador idempotente");
        Pass((await Data(admin)).ScheduledOrders.Single().PaidAmount==150,"retry não duplica pagamento");
        Pass((await Data(admin)).ScheduledOrders.Single().Stage=="Produção","recebimento não entrega pedido automaticamente");
        payment.Data.ScheduledOrders.Single().Notes="Payload diferente";await Expect(await employee.PostAsJsonAsync("commit",payment),HttpStatusCode.BadRequest,"mesmo identificador e outro payload recusados");
        await Change(admin,d=>{var o=d.ScheduledOrders.Single();var sale=new Sale{Product="Parcela",CustomerName=o.CustomerName,TotalValue=100,PaymentType="PIX"};d.Sessions.Single(s=>s.IsOpen).Sales.Add(sale);o.Payments.Add(new(){Amount=100,SaleId=sale.Id,PaymentType="PIX"});o.FullyPaid=true;},"pagamento acima do saldo recusado",HttpStatusCode.BadRequest);
        await Change(employee,d=>d.ScheduledOrders.Single().Notes="alteração sem motivo","edição após recebimento exige justificativa",HttpStatusCode.BadRequest);
        await Change(admin,d=>{d.ScheduledOrders.Single().Notes="cliente solicitou";d.AuditTrail.Add(new(){EntityId=order.Id.ToString(),Reason="Cliente confirmou a alteração"});},"editar pedido recebido com motivo");
        var saleId=(await Data(admin)).ScheduledOrders.Single().Payments.Single().SaleId!.Value;
        await Change(employee,d=>{var sale=d.Sessions.SelectMany(s=>s.Sales).Single(x=>x.Id==saleId);sale.PaymentType="Dinheiro";d.ScheduledOrders.Single().Payments.Single().PaymentType="Dinheiro";d.AuditTrail.Add(new(){EntityId=saleId.ToString(),Reason="Cliente confirmou dinheiro"});},"correção financeira sem permissão 403",HttpStatusCode.Forbidden);
        await Change(admin,d=>{var sale=d.Sessions.SelectMany(s=>s.Sales).Single(x=>x.Id==saleId);var draft=JsonSerializer.Deserialize<Sale>(JsonSerializer.Serialize(sale))!;draft.PaymentType="Dinheiro";FinancialOperations.EditSale(d,sale,draft,"Cliente confirmou dinheiro",OperationContext.Administrator("Forjado"));},"corrigir forma com justificativa");
        await Change(admin,d=>FinancialOperations.CorrectSale(d,saleId,140,"Cliente confirmou valor menor",OperationContext.Administrator("Forjado")),"corrigir valor preservando original");
        Pass((await Data(admin)).FinancialCorrections.Count==1,"correção financeira vinculada persistida");
        await Change(employee,d=>d.Sessions.Single(s=>s.IsOpen).Withdrawals.Add(new(){Amount=10,Reason="Retirada para despesas",UserName="Falso"}),"sangria sem permissão 403",HttpStatusCode.Forbidden);
        await Change(admin,d=>d.Sessions.Single(s=>s.IsOpen).Withdrawals.Add(new(){Amount=10000,Reason="Retirada para despesas"}),"sangria acima do saldo recusada",HttpStatusCode.BadRequest);
        await Change(admin,d=>CashOperations.Withdraw(d,cash.Id,new(){Amount=10,Reason="Retirada para despesas"},OperationContext.Administrator("Forjado")),"sangria válida");
        await Change(admin,d=>FinancialOperations.CloseCash(d,cash.Id,d.Sessions.Single(s=>s.Id==cash.Id),230,OperationContext.Administrator("Forjado")),"fechar caixa");
        await Change(admin,d=>{var c=d.Sessions.Single(s=>s.Id==cash.Id);c.ClosedAt=DateTime.Now.AddMinutes(1);},"segundo fechamento sem retificação recusado",HttpStatusCode.BadRequest);
        await Change(admin,d=>d.Products.Add(new Product{Description="Produto custo protegido",Price=10,Cost=5,ExactQuantityPrices=[new(){Quantity=10,TotalPrice=100,TotalCost=50}]}),"cadastrar produto com custo");
        await Rules(null,"Financeiro",new(){{"view.products",false}});Pass(!(await Data(employee)).Products.Any(),"dados de módulo oculto não são expostos");
        await Change(employee,d=>d.Products.Add(new Product{Description="Injetado",Price=10}),"módulo oculto não aceita injeção 403",HttpStatusCode.Forbidden);
        await Rules(maria.Id,null,new(){{"view.products",true}});var panel=(await admin.GetFromJsonAsync<List<PermissionState>>($"auth/permission-panel?userId={maria.Id}"))!;Pass(panel.Single(p=>p.Key=="view.products").Origin=="✓ Exceção individual","origem perfil / exceção identificada");
        await Change(employee,d=>d.Products.Single().ExactQuantityPrices[0].TotalCost=80,"custo por quantidade protegido 403",HttpStatusCode.Forbidden);
        await Expect(await admin.PostAsJsonAsync("receipts/authorize",new ReceiptAuditRequest(Guid.NewGuid(),"Cliente",50,"PIX","Serviço")),HttpStatusCode.OK,"recibo autorizado e auditado no servidor");
        var events=(await admin.GetFromJsonAsync<List<AuditEntry>>($"audit?entity=Pedido&id={order.Id}"))!;Pass(events.Any(e=>e.Action=="Pagamento recebido"&&e.UserId==maria.Id.ToString()&&e.UserLogin=="maria")&&events.Any(e=>e.Action=="Pedido entregue"),"Histórico retorna pagamento e entrega com identidade estável");
        await Expect(await employee.GetAsync($"audit?entity=Pedido&id={order.Id}"),HttpStatusCode.OK,"histórico pontual permitido sem auditoria administrativa");
        var audit=(await admin.GetFromJsonAsync<List<AuditEntry>>("audit?module=Financeiro"))!;Pass(audit.Any(a=>a.Action.Contains("Sangria"))&&audit.All(a=>a.UserName!="Forjado"),"auditoria financeira usa identidade do servidor");
        var secret=JsonSerializer.Serialize(await Data(admin));Pass(!secret.Contains(Convert.ToBase64String(LoginSecurity.Create(Password).Hash))&&!secret.Contains("password_hash")&&!(await Data(admin)).Prompt12.LoginPassword.HasValue(),"API não expõe credenciais");
        await Expect(await admin.PostAsJsonAsync("auth/users",new UserEdit(maria.Id,"Maria Atualizada","maria","Financeiro",false,null)),HttpStatusCode.OK,"editar e desativar usuário");await Expect(await employee.GetAsync("data"),HttpStatusCode.Unauthorized,"sessão desativada recusada imediatamente");
        await Expect(await employee.PostAsJsonAsync("commit",payment),HttpStatusCode.Unauthorized,"usuário desativado não executa operação crítica");
        Pass(store.Load().AuditTrail.Any(e=>e.UserName=="Maria Teste"&&e.UserId==maria.Id.ToString()),"desativação preserva snapshot de autoria antiga");
        await Expect(await admin.PostAsJsonAsync("auth/users",new UserEdit(maria.Id,"Maria Atualizada","maria","Administrador",true,"Nova7B-Senha!456")),HttpStatusCode.OK,"reativar, alterar perfil e redefinir senha");await Login(employee,"maria","Nova7B-Senha!456");
        await Expect(await admin.PostAsJsonAsync("auth/revoke",new SessionRevoke(maria.Id)),HttpStatusCode.OK,"encerrar sessões");await Expect(await employee.GetAsync("auth/me"),HttpStatusCode.Unauthorized,"sessão encerrada não reutiliza token");
        await Expect(await admin.PostAsJsonAsync("auth/users",new UserEdit(maria.Id,"Maria Atualizada","maria","Administrador",false,null)),HttpStatusCode.OK,"desativar segundo administrador para testar proteção");
        await Expect(await admin.PostAsJsonAsync("auth/permissions",new PermissionEdit(owner.User.UserId,null,new(){{"system.users",false}})),HttpStatusCode.BadRequest,"último administrador recuperável protegido");
        Console.WriteLine($"FASE7B: {count} PASS; 0 FAIL");
    }
    static bool HasValue(this OwnerPinCredential? value)=>value!=null;
}

