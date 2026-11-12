using GraficaCaixa;
using GraficaCaixa.Wpf;

// WhatsApp: Pix copia e cola (BR Code), modelos de mensagem, "pronto desde" do pedido e textos montados para o envio.
public static class WhatsAppTests
{
    static int passed;
    static void Check(bool ok,string name){if(!ok)throw new Exception("FALHOU: "+name);Console.WriteLine("OK "+name);passed++;}
    static readonly OperationContext Admin=OperationContext.Administrator("Teste WhatsApp");
    public static void Run()
    {
        // Exemplo do manual do BR Code (Banco Central): mesmo conteúdo, CRC 1D3D.
        const string official="00020126580014br.gov.bcb.pix0136123e4567-e12b-12d1-a456-4266554400005204000053039865802BR5913Fulano de Tal6008BRASILIA62070503***6304";
        Check(PixBrCode.Crc16(official).ToString("X4")=="1D3D","CRC16 do BR Code igual ao exemplo oficial (1D3D)");

        var code=PixBrCode.Build("123e4567-e12b-12d1-a456-426655440000","Gráfica Rápida Ctrl A Ctrl P","São Paulo",150.5m,"PED-1042");
        Check(code.StartsWith("000201")&&code.Contains("0014br.gov.bcb.pix")&&code.Contains("5406150.50")&&code.Contains("5802BR"),"BR Code com chave e valor");
        Check(code.Contains("5925GRAFICA RAPIDA CTRL A CT")&&code.Contains("6009SAO PAULO"),"nome e cidade sem acento e no tamanho do padrão");
        Check(code.Contains("0507PED1042"),"identificador do pedido no Pix (só letras e números)");
        Check(code[^4..]==PixBrCode.Crc16(code[..^4]).ToString("X4"),"código de verificação confere");
        Check(!PixBrCode.Build("a@b.com","X","Y",0).Contains("5404")&&!PixBrCode.Build("a@b.com","X","Y",0).Contains("54"+"0"),"sem valor: campo 54 não entra");

        Check(PixBrCode.NormalizeKey("123.456.789-09")=="12345678909"&&PixBrCode.Validate("123.456.789-09")==null,"CPF formatado vira só dígitos");
        Check(PixBrCode.NormalizeKey("(11) 98765-1002")=="+5511987651002"&&PixBrCode.Validate("(11) 98765-1002")==null,"telefone vira +55");
        Check(PixBrCode.NormalizeKey(" Contato@Grafica.com ")=="contato@grafica.com","e-mail em minúsculas");
        Check(PixBrCode.NormalizeKey("11.222.333/0001-81")=="11222333000181","CNPJ formatado vira só dígitos");
        Check(PixBrCode.Validate("111.111.111-11")!=null&&PixBrCode.Validate("abc")!=null&&PixBrCode.Validate("")==null,"chave inválida é recusada; vazia é permitida");

        var company=new CompanyProfile{Name="Gráfica",City="São Paulo",PixKey="contato@grafica.com"};
        Check(PixBrCode.MessageBlock(company,0)==""&&PixBrCode.MessageBlock(new CompanyProfile(),50)=="","sem saldo ou sem chave: nada de Pix na mensagem");
        Check(PixBrCode.MessageBlock(company,50,"PED7").Contains("R$ 50,00")&&PixBrCode.MessageBlock(company,50).Contains("br.gov.bcb.pix"),"bloco Pix com valor e código");

        var names=CommunicationOperations.DefaultTemplates().Select(x=>x.Name).ToList();
        Check(new[]{"Pedido pronto","Lembrete de retirada","Envio de recibo","Envio de orçamento","Cobrança de devedores"}.All(names.Contains),"modelos padrão do WhatsApp existem");
        var saved=new Prompt12State{CommunicationTemplates=[new(){Name="Pedido pronto",Body="Meu texto {pix}"}]};
        Check(CommunicationOperations.EffectiveTemplates(saved).Count(x=>x.Name=="Pedido pronto")==1&&CommunicationOperations.EffectiveTemplates(saved).Count==names.Count,"modelo salvo substitui o padrão de mesmo nome; os outros continuam");

        // Pronto desde: preenchido ao entrar em "Pronto para retirada", mantido enquanto continuar pronto e apagado ao sair.
        var order=new ScheduledOrder{Number=7,CustomerName="Cliente",Contact="11 98765-1002",OrderTotal=100,OriginalTotal=100,Stage="Produção",ProductionBusinessDays=3};
        var data=new AppData{ScheduledOrders=[order],FinancialIntegrityVersion=1,OrderDetailsVersion=4,Company=company};
        OrderOperations.AdvanceStage(data,order.Id,order.Revision,OrderStages.Ready,Admin,false);
        Check(order.ReadyAt?.Date==DateTime.Today,"ao ficar pronto, guarda a data");
        var first=order.ReadyAt;var draft=System.Text.Json.JsonSerializer.Deserialize<ScheduledOrder>(System.Text.Json.JsonSerializer.Serialize(order))!;draft.Description="ajuste";draft.ReadyAt=null;
        OrderOperations.Save(data,draft,Admin);var savedOrder=data.ScheduledOrders.Single(x=>x.Id==order.Id);
        Check(savedOrder.ReadyAt==first,"editar um pedido pronto mantém a data (o cliente não consegue mudar)");
        OrderOperations.AdvanceStage(data,savedOrder.Id,savedOrder.Revision,"Produção",Admin,false);
        Check(data.ScheduledOrders.Single(x=>x.Id==order.Id).ReadyAt==null,"ao sair de pronto, a data some");
        var details=new OrderDetails(new ScheduledOrder{ReadyAt=new DateTime(2026,10,1)});var back=new ScheduledOrder();details.Apply(back);
        Check(back.ReadyAt==new DateTime(2026,10,1),"data de pronto é gravada e lida no banco (detalhes do pedido)");

        var ready=new ScheduledOrder{Number=12,CustomerName="Maria",Contact="11 98765-1002",OrderTotal=80,OriginalTotal=80,Stage=OrderStages.Ready,ReadyAt=new DateTime(2026,10,10)};
        var msgData=new AppData{Company=company};
        var pickup=WhatsAppActions.Message(msgData,CommunicationOperations.PickupTemplateName,WhatsAppActions.OrderValues(msgData,ready));
        Check(pickup.Contains("Maria")&&pickup.Contains("#12")&&pickup.Contains("10/10")&&pickup.Contains("br.gov.bcb.pix")&&pickup.Contains("*Gráfica*"),"lembrete de retirada com nome, pedido, data e Pix");
        var noPix=WhatsAppActions.Message(new AppData{Company=new CompanyProfile{Name="Gráfica"}},CommunicationOperations.ReadyTemplateName,WhatsAppActions.OrderValues(new AppData{Company=new CompanyProfile{Name="Gráfica"}},ready));
        Check(noPix=="Olá Maria, o pedido #12 está pronto para retirada. Saldo: R$ 80,00.","sem chave Pix a mensagem fica limpa");
        Check(WhatsAppActions.ReadySince(new ScheduledOrder{DueDate=new DateTime(2026,10,5)})==new DateTime(2026,10,5),"pedido pronto antes desta versão usa o prazo como referência");
        // Ficha em PDF não é edição: não muda a revisão (2º PDF e "Salvar alterações" no editor aberto continuam valendo).
        var pdfOrder=new ScheduledOrder{Number=20,CustomerName="Andressa",OrderTotal=84,OriginalTotal=84,Stage=OrderStages.Ready};var pdfData=new AppData{ScheduledOrders=[pdfOrder],FinancialIntegrityVersion=1,OrderDetailsVersion=4};
        var editorCopy=System.Text.Json.JsonSerializer.Deserialize<ScheduledOrder>(System.Text.Json.JsonSerializer.Serialize(pdfOrder))!;var revision=pdfOrder.Revision;
        OrderOperations.MarkPdfGenerated(pdfData,[pdfOrder.Id],DateTime.Now);OrderOperations.MarkPdfGenerated(pdfData,[pdfOrder.Id],DateTime.Now);
        Check(pdfOrder.Revision==revision&&pdfOrder.PdfGeneratedAt!=null,"gerar a ficha duas vezes não muda a revisão do pedido");
        editorCopy.Description="ajuste depois do PDF";OrderOperations.Save(pdfData,editorCopy,Admin);
        Check(pdfData.ScheduledOrders.Single().PdfGeneratedAt!=null,"salvar o pedido depois do PDF funciona e mantém \"PDF gerado\"");
        // Mudar só a etapa de um pedido pago não pede justificativa (casos reais dos pedidos #1, #29 e #51).
        var paidBefore=new ScheduledOrder{Number=51,OrderTotal=2157,OriginalTotal=null,ItemCostsEnabled=true,Payments=[new(){Amount=100,SaleId=Guid.NewGuid()}],Items=[new(){Quantity=4,UnitCost=39.0m,UnitPrice=120m},new(){Quantity=1}]};
        var paidAfter=System.Text.Json.JsonSerializer.Deserialize<ScheduledOrder>(System.Text.Json.JsonSerializer.Serialize(paidBefore))!;paidAfter.Stage="Produção";paidAfter.OriginalTotal=2157;paidAfter.Items[0].UnitCost=39.00m;paidAfter.Items[0].UnitPrice=120.00m;
        Check(!OrderOperations.RequiresPaidCorrection(paidBefore,paidAfter),"39,0 = 39,00 e valor original vazio = total: sem justificativa");
        var legacy=new ScheduledOrder{OrderTotal=32,Payments=[new(){Amount=32,SaleId=Guid.NewGuid()}],Items=[new(){Quantity=1}]};var legacyAfter=System.Text.Json.JsonSerializer.Deserialize<ScheduledOrder>(System.Text.Json.JsonSerializer.Serialize(legacy))!;legacyAfter.ItemCostsEnabled=true;legacyAfter.OriginalTotal=32;
        Check(!OrderOperations.RequiresPaidCorrection(legacy,legacyAfter),"pedido antigo sem custo: ligar \"custo por item\" não conta como alteração");
        var realChange=System.Text.Json.JsonSerializer.Deserialize<ScheduledOrder>(System.Text.Json.JsonSerializer.Serialize(paidAfter))!;realChange.Items[0].UnitCost=40m;
        Check(!OrderOperations.RequiresPaidCorrection(paidBefore,realChange),"custo é controle interno: mudar custo de pedido pago não pede justificativa");
        var moreItems=System.Text.Json.JsonSerializer.Deserialize<ScheduledOrder>(System.Text.Json.JsonSerializer.Serialize(paidAfter))!;moreItems.Items.Add(new(){Quantity=1,UnitPrice=50m});moreItems.OrderTotal=2207;
        Check(!OrderOperations.RequiresPaidCorrection(paidBefore,moreItems),"incluir item (total sobe) em pedido pago não pede justificativa");
        var belowPaid=System.Text.Json.JsonSerializer.Deserialize<ScheduledOrder>(System.Text.Json.JsonSerializer.Serialize(paidAfter))!;belowPaid.OrderTotal=80;
        Check(OrderOperations.RequiresPaidCorrection(paidBefore,belowPaid),"total abaixo do já recebido (R$ 80 < R$ 100) pede justificativa");
        var undefinedValue=System.Text.Json.JsonSerializer.Deserialize<ScheduledOrder>(System.Text.Json.JsonSerializer.Serialize(paidAfter))!;undefinedValue.PricePending=true;
        Check(OrderOperations.RequiresPaidCorrection(paidBefore,undefinedValue),"pedido pago voltando a \"valor a definir\" pede justificativa");
        // Lançamento novo com caixa aberto em outro dia: data da abertura, horário de agora, nunca antes da abertura.
        var cashDate=typeof(MainWindow).GetMethod("CashDate",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!;
        DateTime? Day(DateTime opened)=>(DateTime?)cashDate.Invoke(null,[new CashSession{OpenedAt=opened}]);
        Check(Day(DateTime.Now.AddMinutes(-5))==null,"caixa aberto hoje: data e hora de agora");
        var yesterday=Day(DateTime.Today.AddDays(-1).AddHours(8));
        Check(yesterday?.Date==DateTime.Today.AddDays(-1),"caixa de ontem não fechado: lançamento entra na data de ontem");
        var lateOpen=DateTime.Today.AddDays(-1).AddHours(23).AddMinutes(59);
        Check(Day(lateOpen)>=lateOpen&&Day(lateOpen)?.Date==lateOpen.Date,"horário nunca fica antes da abertura do caixa");
        // Servidor: pedido pago indo para "Pronto para retirada" (caso do #130) passa sem justificativa; total abaixo do recebido continua exigindo.
        var actor=new UserIdentity(Guid.NewGuid(),"Teste","teste","Administrador",Guid.NewGuid());var allowed=Permissions.Catalog.ToDictionary(x=>x.Key,_=>true);
        var paidOrder=new ScheduledOrder{Number=130,CustomerName="Mobile",Contact="11979984998",OrderTotal=50,Stage="Produção",ProductionBusinessDays=3,Payments=[new(){Amount=20,SaleId=Guid.NewGuid()}],Items=[new(){Quantity=1,Product="Impressão",UnitPrice=50m,UnitCost=3.5m}]};
        var serverBefore=new AppData{ScheduledOrders=[paidOrder]};
        AppData Edited(Action<ScheduledOrder> change){var copy=System.Text.Json.JsonSerializer.Deserialize<AppData>(System.Text.Json.JsonSerializer.Serialize(serverBefore))!;change(copy.ScheduledOrders[0]);return copy;}
        bool Accepted(AppData next){try{ServerAuthorization.ValidateAndAudit(serverBefore,next,actor,allowed);return true;}catch(InvalidOperationException ex){Console.WriteLine("   recusado: "+ex.Message);return false;}}
        Check(Accepted(Edited(o=>{o.Stage=OrderStages.Ready;o.ReadyAt=DateTime.Now;o.PayAfterDelivery=true;o.OrderTotal=50.00m;o.OriginalTotal=50.00m;o.Items[0].UnitCost=3.50m;o.DebtOriginDate=o.CreatedAt.Date;o.Revision++;})),"servidor: pronto para retirada + campos completados pelo editor, sem justificativa");
        Check(Accepted(Edited(o=>{o.OrderTotal=60m;o.OriginalTotal=60m;o.Items.Add(new(){Quantity=1,Product="Arte",UnitPrice=10m});o.Revision++;})),"servidor: incluir item em pedido pago (total sobe) passa sem justificativa");
        Check(!Accepted(Edited(o=>{o.OrderTotal=15m;o.OriginalTotal=15m;o.Revision++;})),"servidor: total abaixo do já recebido (R$ 15 < R$ 20) continua exigindo justificativa");
        Console.WriteLine($"WHATSAPP: {passed} testes aprovados");
    }
}
