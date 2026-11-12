using GraficaCaixa;

// Limpeza de Clientes: sai quem não tem telefone completo com DDD; número completo sem nome fica; pedidos e lançamentos não mudam.
public static class CustomerPurgeTests
{
    static int passed;
    static void Check(bool ok,string name){if(!ok)throw new Exception("FALHOU: "+name);Console.WriteLine("OK "+name);passed++;}
    static T Clone<T>(T value)=>System.Text.Json.JsonSerializer.Deserialize<T>(System.Text.Json.JsonSerializer.Serialize(value))!;
    public static void Run()
    {
        var user=OperationContext.Administrator("Teste");
        var data=new AppData{Customers=[new CustomerProfile{Name="Patricia",Contact="11963145682"},new CustomerProfile{Name="Patricia",Contact="1881"},new CustomerProfile{Name="4546",Contact="Não informado"},new CustomerProfile{Name="Pri",Contact=""},new CustomerProfile{Name="",Contact="(11) 96812-9723"},new CustomerProfile{Name="5139",Contact="5139"}],
            Sessions=[new CashSession{Sales=[new Sale{CustomerName="Pri",Product="Cópia",Units=1,UnitValue=1,PaymentType="PIX"}]}]};
        try{CustomerBasicOperations.DeleteWithoutFullPhone(data,3,user);Check(false,"contagem diferente recusada");}catch(InvalidOperationException){Check(data.Customers.Count==6,"contagem diferente da mostrada não exclui nada");}
        Check(CustomerBasicOperations.DeleteWithoutFullPhone(data,4,user)==4,"exclui os 4 sem telefone completo");
        Check(data.Customers.Count==2&&data.Customers.All(CustomerBasicOperations.HasFullPhone)&&data.Customers.Any(x=>x.Name==""),"ficam os de número completo, inclusive sem nome");
        Check(data.Sessions[0].Sales.Single().CustomerName=="Pri"&&data.AuditTrail.Count(x=>x.Action=="Cliente excluído")==4,"lançamento mantém o nome e cada exclusão vai para a auditoria");
        CustomerBasicOperations.SyncFromRecords(data,DateTime.Today.AddDays(-1));Check(data.Customers.All(x=>x.Name!="Pri"),"cadastro excluído não volta sozinho");
        // Cadastro automático só com telefone completo: balcão sem número ou com 4 dígitos não vira cliente.
        data.Sessions[0].Sales.AddRange([new Sale{CustomerName="Rua",Product="Cópia",Units=1,UnitValue=1,PaymentType="PIX"},new Sale{CustomerName="Pereira",Contact="0946",Product="Cópia",Units=1,UnitValue=1,PaymentType="PIX"},new Sale{CustomerName="Jeani",Contact="(11) 95753-6198",Product="Cópia",Units=1,UnitValue=1,PaymentType="PIX"}]);
        CustomerBasicOperations.SyncFromRecords(data,DateTime.Today.AddDays(-1));
        Check(data.Customers.Any(x=>x.Name=="Jeani")&&data.Customers.All(x=>x.Name!="Rua"&&x.Name!="Pereira"),"lançamento só cria cadastro com telefone completo");
        // Telefones repetidos: bloqueio no cadastro/edição e mesclagem.
        var hbr=new CustomerProfile{Name="ACME",Contact="11958121184"};var julia=new CustomerProfile{Name="ACME - Julia",Contact="(11) 95812-1184",Email="julia@acme.example",CpfCnpj="12.345.678/0001-90"};var other=new CustomerProfile{Name="Alan",Contact="11995757445"};
        var shop=new AppData{Customers=[hbr,julia,other]};
        var order=new ScheduledOrder{Number=1,CustomerName="ACME - Julia",Contact="11958121184",CustomerId=julia.Id,Revision=3};var debt=new ManualDebt{CustomerName="ACME - Julia",Contact="11958121184",Amount=10};shop.ScheduledOrders.Add(order);shop.ManualDebts.Add(debt);
        Check(CustomerBasicOperations.PhoneDuplicates(shop).Single().Count==2,"telefones repetidos agrupados (com e sem máscara)");
        try{CustomerBasicOperations.Create(shop,Guid.NewGuid(),"Outro nome","+55 11 95812-1184","","","",user);Check(false,"novo cliente com telefone existente recusado");}catch(InvalidOperationException ex){Check(ex.Message.StartsWith(CustomerBasicOperations.PhoneTakenPrefix)&&ex.Message.Contains("ACME"),"novo cliente com telefone de outro é bloqueado e diz de quem é");}
        try{CustomerBasicOperations.Edit(shop,Clone(other),"Alan","11958121184",user);Check(false,"edição para telefone existente recusada");}catch(InvalidOperationException ex){Check(ex.Message.StartsWith(CustomerBasicOperations.PhoneTakenPrefix),"trocar o telefone para o de outro cliente é bloqueado");}
        CustomerBasicOperations.Edit(shop,Clone(hbr),"HBR Gráfica","11958121184",user);hbr.Name="ACME";Check(true,"repetido antigo continua editável sem trocar o telefone");
        try{CustomerBasicOperations.Merge(shop,hbr.Id,[other.Id],user);Check(false,"mesclar telefones diferentes");}catch(InvalidOperationException){Check(shop.Customers.Count==3,"mesclar só aceita o mesmo telefone");}
        CustomerBasicOperations.Merge(shop,hbr.Id,[julia.Id],user);
        Check(shop.Customers.Count==2&&shop.Customers.All(x=>x.Id!=julia.Id)&&hbr.Email=="julia@acme.example"&&hbr.CpfCnpj.Length>0,"fica um cadastro, com e-mail e CNPJ que faltavam");
        Check(order.CustomerId==hbr.Id&&order.CustomerName=="ACME"&&order.Revision==4&&debt.CustomerId==hbr.Id&&debt.CustomerName=="ACME","pedido e dívida passam para o cadastro que fica, com o nome dele");
        Check(shop.AuditTrail.Any(x=>x.Action=="Clientes mesclados")&&shop.AuditTrail.Any(x=>x.Action=="Cliente excluído"),"mesclagem e exclusão registradas na auditoria");
        // Telefone incompleto (ex.: só 4 dígitos) é recusado; em branco continua valendo (cliente de balcão sem número).
        Check(PhoneText.IsCompleteOrEmpty("")&&PhoneText.IsCompleteOrEmpty("(11) 95202-8478")&&PhoneText.IsCompleteOrEmpty("1133334444")&&PhoneText.IsCompleteOrEmpty("+55 11 95202-8478")&&PhoneText.IsCompleteOrEmpty("+351 912 345 678"),"vazio, celular, fixo, com 55 e internacional aceitos");
        Check(!PhoneText.IsCompleteOrEmpty("1881")&&!PhoneText.IsCompleteOrEmpty("95202-8478")&&!PhoneText.IsCompleteOrEmpty("119520270171"),"4 dígitos, sem DDD e número a mais recusados");
        try{CustomerBasicOperations.Create(shop,Guid.NewGuid(),"Rua","1881","","","",user);Check(false,"novo cliente com 4 dígitos");}catch(InvalidOperationException ex){Check(ex.Message==PhoneText.IncompleteMessage,"novo cliente com 4 dígitos é recusado");}
        CustomerBasicOperations.Create(shop,Guid.NewGuid(),"Rua","","","","",user);Check(shop.Customers.Any(x=>x.Name=="Rua"&&x.Contact==""),"cliente de balcão sem telefone continua aceito");
        try{CustomerBasicOperations.Edit(shop,Clone(other),"Alan","9575",user);Check(false,"editar para 4 dígitos");}catch(InvalidOperationException ex){Check(ex.Message==PhoneText.IncompleteMessage,"trocar o telefone por 4 dígitos é recusado");}
        // Totais por cadastro: homônimos não se misturam; telefone identifica o cliente mesmo com nome diferente; nome antigo de mescla continua somando.
        var ana1=new CustomerProfile{Name="Adriana",Contact="11976340005"};var ana2=new CustomerProfile{Name="Adriana",Contact="11925888408"};var solo=new CustomerProfile{Name="Pedro",Contact=""};var prime=new CustomerProfile{Name="Alfa Pragas",Contact="(11) 96812-9723",MergedNames="Alfa Praga"};var shared1=new CustomerProfile{Name="Amanda",Contact="11942517200"};var shared2=new CustomerProfile{Name="Vanessa",Contact="11942517200"};
        var who=new CustomerRecords([ana1,ana2,solo,prime,shared1,shared2]);
        Check(who.Owner(null,"Adriana","11976340005")==ana1.Id&&who.Owner(null,"adriana","(11) 92588-8408")==ana2.Id,"homônimos: cada Adriana só com o próprio telefone");
        Check(who.Owner(null,"Adriana","")==null&&who.Owner(null,"Adriana","11999998888")==null,"homônimo sem telefone ou com telefone de outro não é somado em nenhuma");
        Check(who.Owner(null,"Prime","11968129723")==prime.Id&&who.Owner(null,"Alfa Praga","")==prime.Id,"telefone e nome antigo da mescla somam no cadastro que ficou");
        Check(who.Owner(null,"Pedro","")==solo.Id&&who.Owner(null,"Pedro","11955554444")==solo.Id&&who.Owner(solo.Id,"Outro","11976340005")==solo.Id,"nome único vale; vínculo do pedido manda");
        Check(who.Owner(null,"Vanessa","11942517200")==shared2.Id&&who.Owner(null,"Fulano","11942517200")==null,"telefone em dois cadastros decide pelo nome");
        Check(who.Owner(null,"Cliente Balcão","")==null&&who.Owner(null,"Não informado","")==null,"balcão não pertence a ninguém");
        var mergeData=new AppData{Customers=[new CustomerProfile{Name="ACME",Contact="11958121184"},new CustomerProfile{Name="ACME - Julia",Contact="11958121184"}]};var keepId=mergeData.Customers[0].Id;CustomerBasicOperations.Merge(mergeData,keepId,[mergeData.Customers[1].Id],user);
        Check(mergeData.Customers.Single().MergedNames=="ACME - Julia"&&new CustomerRecords(mergeData.Customers).Owner(null,"ACME - Julia","")==keepId,"mesclar lembra o nome antigo para os lançamentos do caixa");
        Console.WriteLine($"TOTAL: {passed} verificações da limpeza de clientes.");
    }
}
