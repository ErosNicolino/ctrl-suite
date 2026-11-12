using GraficaCaixa;

static class Test
{
    static readonly OperationContext Admin=OperationContext.Administrator("Teste Admin");
    static int passed;
    static void Assert(bool condition,string name){if(!condition)throw new Exception("FALHOU: "+name);Console.WriteLine("OK  "+name);passed++;}
    static void Throws<T>(Action action,string name,string? contains=null) where T:Exception{try{action();throw new Exception("FALHOU (não lançou): "+name);}catch(T ex){if(contains!=null&&!ex.Message.Contains(contains,StringComparison.OrdinalIgnoreCase))throw;Console.WriteLine("OK  "+name);passed++;}}
    static (AppData Data,ScheduledOrder Order,CashSession Cash) Scenario(decimal total)
    {
        var cash=new CashSession{OpeningAmount=100,OpenedBy=Admin.UserName};var order=new ScheduledOrder{Number=100,OrderTotal=total,OriginalTotal=total,CustomerName="Cliente",Description="Produto",Stage="Pronto para retirada"};var data=new AppData{Sessions=[cash],ScheduledOrders=[order],FinancialIntegrityVersion=1,OrderDetailsVersion=4};return(data,order,cash);
    }
    [STAThread]
    public static void Main(string[] args)
    {
        if(args.Length==3&&args[0]=="--phase7c"){try{Phase7CTests.Run(args[1],args[2]);}catch(Exception ex){Console.WriteLine(ex);Environment.ExitCode=1;}return;}
        if(args.Length==3&&args[0]=="--phase7b-migration"){try{Phase7BMigrationTests.Run(args[1],args[2]);}catch(Exception ex){Console.WriteLine(ex);Environment.ExitCode=1;}return;}
        if(args.Length==2&&args[0]=="--phase7b-ui"){try{Phase7BUiTests.Run(args[1]);}catch(Exception ex){Console.WriteLine(ex);Environment.ExitCode=1;}return;}
        if(args.Length==2&&args[0]=="--phase7b"){try{Phase7BTests.Run(args[1]);}catch(Exception ex){Console.WriteLine(ex);Environment.ExitCode=1;}return;}
        if(args.Length==3&&args[0]=="--phase7a"){try{Phase7ATests.Run(args[1],args[2]);}catch(Exception ex){Console.WriteLine(ex);Environment.ExitCode=1;}return;}
        if(args.Length==1&&args[0]=="--phase7a-network"){NetworkConnectionTests.Run();return;}
        if(args.Length==2&&args[0]=="--phase7a-fixture"){var fixture=new DataStore(args[1]);fixture.EnsureInitialized();fixture.Update(d=>{d.Prompt12.LoginPassword=LoginSecurity.Create("Teste7A-Senha!123");return 0;});Console.WriteLine("FIXTURE OK");return;}
        if(args.Contains("--phase1-focus"))
        {
            var quantity=new System.Windows.Controls.TextBox{Text="25"};
            var next=new System.Windows.Controls.TextBox();
            GraficaCaixa.Wpf.QuantityInput.ClearOnEntry(quantity);
            var panel=new System.Windows.Controls.StackPanel();panel.Children.Add(quantity);panel.Children.Add(next);
            var window=new System.Windows.Window{Content=panel,Width=240,Height=120,ShowInTaskbar=false};
            try
            {
                window.Show();
                quantity.Focus();Assert(quantity.Text=="25","quantidade mantida ao receber foco");
                next.Focus();quantity.Focus();Assert(quantity.Text=="25","quantidade mantida após TAB e SHIFT+TAB");
                var click=new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0,System.Windows.Input.MouseButton.Left){RoutedEvent=System.Windows.UIElement.PreviewMouseLeftButtonDownEvent};
                quantity.RaiseEvent(click);Assert(quantity.Text=="25","quantidade mantida ao clicar novamente");
            }
            finally{window.Close();}
            return;
        }
        if(args.Contains("--phase1-close"))
        {
            var application=System.Windows.Application.Current??new System.Windows.Application();
            application.Resources["SectionTitle"]=new System.Windows.Style(typeof(System.Windows.Controls.TextBlock));
            application.Resources["SoftButton"]=new System.Windows.Style(typeof(System.Windows.Controls.Button));
            application.Resources["PrimaryButton"]=new System.Windows.Style(typeof(System.Windows.Controls.Button));
            var field=new System.Windows.Controls.TextBox{Text="original"};
            var saved=false;var prompted=false;
            var window=new System.Windows.Window{Content=field,Width=250,Height=120,ShowInTaskbar=false};
            GraficaCaixa.Wpf.UnsavedChanges.Attach(window,()=>saved=true);
            window.Show();field.Text="alterado";
            var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(20)};
            timer.Tick+=(_,_)=>
            {
                var prompt=application.Windows.OfType<GraficaCaixa.Wpf.UnsavedChangesWindow>().FirstOrDefault();
                if(prompt is null)return;
                prompted=true;timer.Stop();
                var buttons=((System.Windows.Controls.StackPanel)prompt.Content).Children.OfType<System.Windows.Controls.WrapPanel>().Single().Children.OfType<System.Windows.Controls.Button>();
                buttons.Single(x=>(string)x.Content=="Descartar").RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            };
            var frame=new System.Windows.Threading.DispatcherFrame();
            window.Closed+=(_,_)=>frame.Continue=false;
            var timeout=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(5)};
            timeout.Tick+=(_,_)=>{timeout.Stop();frame.Continue=false;};
            timer.Start();timeout.Start();window.Close();
            System.Windows.Threading.Dispatcher.PushFrame(frame);
            timer.Stop();timeout.Stop();
            Assert(prompted&&!saved&&!window.IsVisible,"X/Alt+F4 solicita decisão e descartar não salva");
            return;
        }
        if(args.Length==3&&args[0]=="--sqlite-backup"){using var source=new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder{DataSource=args[1],Mode=Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,Pooling=false}.ToString());using var destination=new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder{DataSource=args[2],Mode=Microsoft.Data.Sqlite.SqliteOpenMode.ReadWriteCreate,Pooling=false}.ToString());source.Open();destination.Open();source.BackupDatabase(destination);Console.WriteLine(args[2]);return;}
        if(args.Length==2&&args[0]=="--db-audit"){DbAudit.Run(args[1]);return;}
        if(args.Length==2&&args[0]=="--budget-test"){var data=new DataStore(args[1]).Load();var required=new[]{"adesivo","vinil","banner","lona","papel fotográfico","placa de ps"};foreach(var term in required)if(!data.BudgetRules.Any(x=>x.Product.Contains(term,StringComparison.CurrentCultureIgnoreCase)))throw new Exception("Parâmetro ausente: "+term);var passed=0;foreach(var rule in data.BudgetRules){var width=1.25m;var height=2.4m;var quantity=3m;var expectedPrice=width*height*quantity*rule.SquareMeterRate+rule.NoArtworkFee+rule.ArtworkFee;if(rule.PriceBySize(width,height,quantity)!=expectedPrice)throw new Exception("Cálculo divergente: "+rule.Product);if(rule.MinimumSale(true)!=rule.MinimumCost*rule.MarkupMultiplier+rule.NoArtworkFee+rule.ArtworkFee)throw new Exception("Mínimo divergente: "+rule.Product);passed++;}if(data.Products.Count==0)throw new Exception("Integração com Produtos vazia");Console.WriteLine($"ORCAMENTO_ANTIGO_OK regras={data.BudgetRules.Count}; calculos_aprovados={passed}; produtos_integrados={data.Products.Count}");return;}
        if(args.Length==2&&args[0]=="--migrate-copy"){var store=new DataStore(args[1]);store.EnsureInitialized();var before=store.Load();var counts=(before.Customers.Count,before.Products.Count,before.ScheduledOrders.Count,before.Sessions.Sum(x=>x.Sales.Count),before.AuditTrail.Count,before.Prompt12.RetiredProfessionalQuotes.Count);store.Update(d=>0);var after=store.Load();var afterCounts=(after.Customers.Count,after.Products.Count,after.ScheduledOrders.Count,after.Sessions.Sum(x=>x.Sales.Count),after.AuditTrail.Count,after.Prompt12.RetiredProfessionalQuotes.Count);if(counts!=afterCounts)throw new Exception($"Contagens alteradas: {counts} -> {afterCounts}");Console.WriteLine($"MIGRACAO_OK clientes={after.Customers.Count}; produtos={after.Products.Count}; pedidos={after.ScheduledOrders.Count}; vendas={after.Sessions.Sum(x=>x.Sales.Count)}; auditorias={after.AuditTrail.Count}; orcamentos_inativos={after.Prompt12.RetiredProfessionalQuotes.Count}");return;}
        if(args.Contains("--documents")){DocumentRootResolverTests.Run();return;}
        if(args.Contains("--data-directory")){DataDirectoryResolverTests.Run();return;}
        if(args.Contains("--recovery")){ServerRecoveryTests.Run();return;}
        if(args.Contains("--network")){NetworkConnectionTests.Run();return;}
        if(args.Contains("--reconciliation")){ReconciliationTests.Run();return;}
        if(args.Contains("--whatsapp")){WhatsAppTests.Run();return;}
        if(args.Contains("--owner-pin")){OwnerPinResetTests.Run();return;}
        if(args.Contains("--refund")){RefundTests.Run();return;}
        if(args.Contains("--cash-adjust")){CashAdjustmentTests.Run();return;}
        if(args.Contains("--indicators")){IndicatorsTests.Run();return;}
        if(args.Contains("--today-queue")){TodayQueueTests.Run();return;}
        if(args.Contains("--suppliers")){SupplierTests.Run();return;}
        if(args.Contains("--report-totals")){ReportTotalsTests.Run();return;}
        if(args.Contains("--discount-excess")){DiscountExcessTests.Run();return;}
        if(args.Contains("--fiscal")){FiscalTests.Run();return;}
        if(args.Contains("--price-calc")){PriceCalcTests.Run();return;}
if(args.Contains("--seasonal")){SeasonalTests.Run();return;}
if(args.Contains("--catalog")){WhatsAppCatalogTests.Run();return;}
if(args.Contains("--abc")){AbcCurveTests.Run();return;}
if(args.Contains("--product-photos")){ProductPhotoTests.Run();return;}
        if(args.Contains("--audit-fast")){AuditFastTests.Run();return;}
        if(args.Contains("--budget-cost")){BudgetCostTests.Run();return;}
        if(args.Contains("--forecast")){ForecastTests.Run();return;}
        if(args.Contains("--backup-files")){BackupFileTests.Run();return;}
        if(args.Contains("--tracking")){var at=Array.IndexOf(args,"--tracking");TrackingTests.Run(at+1<args.Length?args[at+1]:null);return;}
        if(args.Contains("--qr")){QrTests.Run(args[Array.IndexOf(args,"--qr")+1]);return;}
        if(args.Contains("--lock-queue")){LockQueueTests.Run();return;}
        if(args.Contains("--read-stress")){ReadStressTests.Run();return;}
        if(args.Contains("--payment-date")){PaymentDateTests.Run();return;}
        if(args.Contains("--expenses")){ExpenseTests.Run();return;}
        if(args.Contains("--roles")){RoleTests.Run();return;}
        if(args.Contains("--customer-purge")){CustomerPurgeTests.Run();return;}
        if(args.Contains("--cert-renewal")){CertificateRenewalTests.Run();return;}
        if(args.Contains("--gateway-writes")){var ok=BrowserGateway.IsSafeCommand("POST","/api/v1/lancamentos")&&BrowserGateway.IsSafeCommand("POST","/api/v1/operacao-pedidos/repasse")&&!BrowserGateway.IsSafeCommand("GET","/api/v1/lancamentos")&&!BrowserGateway.IsSafeCommand("DELETE","/api/v1/lancamentos")&&!BrowserGateway.IsSafeCommand("POST","/api/v1/lancamentos/opcoes")&&!BrowserGateway.IsSafeCommand("POST","/api/v1/caixa/abertura");if(!ok)throw new Exception("FALHOU: comandos liberados no celular");Console.WriteLine("OK Novo lançamento liberado só por POST no portão do celular");return;}
        if(args.Contains("--quantity-pricing")){QuantityPricingTests.Run();return;}
        if(args.Contains("--prompt7")){Prompt7Tests.Run();return;}
        if(args.Contains("--prompt6")){Prompt6Tests.Run();return;}
        if(args.Contains("--small-round")){SmallRoundTests.Run();return;}
if(args.Contains("--stages")){StageTests.Run();return;}if(args.Contains("--debtors")){DebtorsTests.Run();return;}if(args.Contains("--boot")){BootTests.Run().GetAwaiter().GetResult();return;}
        if(args.Contains("--prompt10")){Prompt10Tests.Run();return;}
        if(args.Contains("--hotfix-2026-9-27-2")){Hotfix202609272Tests.Run();return;}
        if(args.Length==2&&args[0]=="--retire-legacy-costs"){var store=new DataStore(args[1]);store.EnsureInitialized();Console.WriteLine("Migração de custo global verificada, sem mover o banco.");return;}
        if(args.Contains("--products-costs")){ProductCostTests.Run();return;}
        if(args.Contains("--visual-refresh")){VisualRefreshTests.Run();return;}
        if(args.Contains("--ux-point-adjustments")){UxPointAdjustmentsTests.Run();return;}
        if(args.Contains("--reminder-search")){ReminderSearchTests.Run();return;}
        if(args.Contains("--order-tabs")){OrderTabsTests.Run();return;}
        if(args.Contains("--cash-sale-link")){CashSaleLinkTests.Run();return;}
        if(args.Contains("--write-perf")){WritePerfProbe.Run(args[Array.IndexOf(args,"--write-perf")+1]);return;}
        if(args.Contains("--migrate-active"))
        {
            var folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"GraficaCaixa","DadosLocais");var store=new DataStore(folder);store.EnsureInitialized();var before=store.Load();var counts=(before.Sessions.Count,before.Sessions.Sum(x=>x.Sales.Count),before.ScheduledOrders.Count,before.AuditTrail.Count);store.Update(d=>0);var after=store.Load();if(counts!=(after.Sessions.Count,after.Sessions.Sum(x=>x.Sales.Count),after.ScheduledOrders.Count,after.AuditTrail.Count))throw new Exception("A migração alterou contagens históricas.");Console.WriteLine($"MIGRADO: sessões={after.Sessions.Count}; lançamentos={after.Sessions.Sum(x=>x.Sales.Count)}; pedidos={after.ScheduledOrders.Count}; auditorias={after.AuditTrail.Count}; versão financeira={after.FinancialIntegrityVersion}");return;
        }
        var paid=Scenario(200);OrderOperations.AddPayment(paid.Data,paid.Order,200,"PIX",DateTime.Now,"Pagamento",Guid.NewGuid(),Admin,paid.Order.Revision);var rev=paid.Order.Revision;OrderOperations.AdvanceStage(paid.Data,paid.Order.Id,rev,"Entregue",Admin,false);Assert(paid.Order.Stage=="Entregue"&&paid.Order.RemainingAmount==0,"entrega totalmente paga");

        var debt=Scenario(200);OrderOperations.AddPayment(debt.Data,debt.Order,50,"PIX",DateTime.Now,"Parcela",Guid.NewGuid(),Admin,debt.Order.Revision);OrderOperations.AdvanceStage(debt.Data,debt.Order.Id,debt.Order.Revision,"Entregue",Admin,true);Assert(debt.Order.Stage=="Entregue"&&debt.Order.RemainingAmount==150&&debt.Order.PayAfterDelivery,"entrega mantém dívida em contas a receber");

        var payments=Scenario(200);OrderOperations.AddPayment(payments.Data,payments.Order,50,"PIX",DateTime.Now,"Parcela",Guid.NewGuid(),Admin,payments.Order.Revision);Assert(payments.Order.RemainingAmount==150,"pagamento parcial");OrderOperations.AddPayment(payments.Data,payments.Order,150,"PIX",DateTime.Now,"Final",Guid.NewGuid(),Admin,payments.Order.Revision);Assert(payments.Order.RemainingAmount==0&&payments.Order.FullyPaid,"pagamento exato");Throws<InvalidOperationException>(()=>OrderOperations.AddPayment(payments.Data,payments.Order,20,"PIX",DateTime.Now,"Excesso",Guid.NewGuid(),Admin,payments.Order.Revision),"pagamento acima do saldo bloqueado","superior ao saldo");

        var idem=Scenario(100);var request=Guid.NewGuid();var first=OrderOperations.AddPayment(idem.Data,idem.Order,40,"PIX",DateTime.Now,"Parcela",request,Admin,idem.Order.Revision);var second=OrderOperations.AddPayment(idem.Data,idem.Order,40,"PIX",DateTime.Now,"Parcela",request,Admin);Assert(first.Id==second.Id&&idem.Cash.Sales.Count==1&&idem.Order.Payments.Count==1,"idempotência de pagamento");

        var correction=Scenario(200);var p=OrderOperations.AddPayment(correction.Data,correction.Order,150,"PIX",DateTime.Now,"Parcela",Guid.NewGuid(),Admin,correction.Order.Revision);var original=correction.Cash.Sales.Single().Total;FinancialOperations.CorrectSale(correction.Data,p.SaleId!.Value,120,"Pagamento lançado incorretamente",Admin,correction.Order.Id);Assert(correction.Cash.Sales.First(x=>x.Id==p.SaleId).Total==original&&correction.Cash.Sales.Count==2&&correction.Order.PaidAmount==120,"correção preserva original e cria ajuste");var audit=correction.Data.AuditTrail.Last();Assert(audit.UserName==Admin.UserName&&audit.Reason.Length>0&&audit.OldValue.Contains("150")&&audit.NewValue.Contains("120"),"auditoria automática da correção");

        var closing=Scenario(80);OrderOperations.AddPayment(closing.Data,closing.Order,80,"Dinheiro",DateTime.Now,"Final",Guid.NewGuid(),Admin,closing.Order.Revision);var expected=System.Text.Json.JsonSerializer.Deserialize<CashSession>(System.Text.Json.JsonSerializer.Serialize(closing.Cash))!;FinancialOperations.CloseCash(closing.Data,closing.Cash.Id,expected,180,Admin);Assert(closing.Data.CashClosureVersions.Count==1&&closing.Data.CashClosureVersions[0].IsOriginal,"fechamento original versionado");var closedExpected=System.Text.Json.JsonSerializer.Deserialize<CashSession>(System.Text.Json.JsonSerializer.Serialize(closing.Cash))!;FinancialOperations.RectifySessionValues(closing.Data,closing.Cash.Id,closedExpected,100,179,closing.Cash.OpenedAt,closing.Cash.ClosedAt!.Value,closing.Cash.OpenedBy,closing.Cash.ClosedBy,"Contagem conferida novamente",Admin);Assert(closing.Data.CashClosureVersions.Count==2&&closing.Data.CashClosureVersions.Last().Version==2&&!closing.Data.CashClosureVersions.Last().IsOriginal,"retificação cria v2 sem destruir v1");

        var denied=Scenario(100);var operatorUser=new OperationContext("Operador","Operador");Throws<UnauthorizedAccessException>(()=>OrderOperations.AddPayment(denied.Data,denied.Order,10,"PIX",DateTime.Now,"Parcela",Guid.NewGuid(),operatorUser,denied.Order.Revision),"permissão bloqueia usuário sem autorização");
        var conflict=Scenario(100);var stale=conflict.Order.Revision;conflict.Order.Revision++;Throws<InvalidOperationException>(()=>OrderOperations.AddPayment(conflict.Data,conflict.Order,10,"PIX",DateTime.Now,"Parcela",Guid.NewGuid(),Admin,stale),"concorrência por revisão do pedido","outro computador");

        var temp=Path.Combine(Path.GetTempPath(),"erp-integrity-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);try{var store=new DataStore(temp);store.EnsureInitialized();store.Update(d=>FinancialOperations.OpenCash(d,10,Admin));Throws<InvalidOperationException>(()=>store.Update<int>(d=>{d.Sessions.Single().Sales.Add(new Sale{Product="Não deve persistir",Units=1,TotalValue=99,PaymentType="PIX"});throw new InvalidOperationException("falha simulada");}),"falha parcial não persiste");Assert(store.Load().Sessions.Single().Sales.Count==0,"rollback lógico antes do commit");}finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(temp,true);}
        var snapshotSource=args.Length==2&&args[0]=="--regression-snapshot"?args[1]:Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"GraficaCaixa","DadosLocais","caixa_dados.db");
        if(!File.Exists(snapshotSource))Console.WriteLine("PULADO  migração sobre cópia do banco local (não encontrado: "+snapshotSource+")");else{
        var productionCopy=Path.Combine(Path.GetTempPath(),"erp-migration-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(productionCopy);try{File.Copy(snapshotSource,Path.Combine(productionCopy,"caixa_dados.db"));var store=new DataStore(productionCopy);store.EnsureInitialized();var before=store.Load();var counts=(before.Sessions.Count,before.Sessions.Sum(x=>x.Sales.Count),before.ScheduledOrders.Count,before.AuditTrail.Count);store.Update(d=>0);var after=store.Load();Assert(counts==(after.Sessions.Count,after.Sessions.Sum(x=>x.Sales.Count),after.ScheduledOrders.Count,after.AuditTrail.Count),"migração segura preserva dados existentes");Assert(after.FinancialIntegrityVersion==1&&after.OrderDetailsVersion==9,"migração ativa estruturas vigentes");}finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(productionCopy,true);}}
        FinancialIntegrity.ValidateState(closing.Data);Console.WriteLine($"TOTAL: {passed} testes aprovados");
    }
}





