using GraficaCaixa;

// Pacote 11 (desempenho): DebtBalances tem de dar exatamente o mesmo resultado das regras originais
// (ManualDebt.Paid/WasReversed/Remaining/IsClosedByCancellation e CollectionControl.Status).
public static class DebtorsTests
{
    static int passed;
    static void Check(bool ok,string name){if(!ok)throw new Exception("FALHOU: "+name);Console.WriteLine("OK "+name);passed++;}
    public static void Run()
    {
        foreach(var seed in new[]{1,7,42,2026,90210})
        {
            var data=Fixture(new Random(seed));
            Compare(data,$"semente {seed}");
        }
        // Casos pontuais: estorno completo, estorno com empate de data (vale a última da lista), cancelada, parcial, venda duplicada.
        var d=new AppData();var session=new CashSession{OpeningAmount=0,OpenedBy="Teste"};d.Sessions.Add(session);var t=new DateTime(2026,10,1,10,0,0);
        var reversed=new ManualDebt{CustomerName="Estornada",Amount=100};var sale=new Sale{Product="Pagamento",TotalValue=0,PaymentGroupId=reversed.Id};session.Sales.Add(sale);
        d.FinancialCorrections.Add(new FinancialCorrection{OriginalSaleId=sale.Id,CorrectedAmount=50,CreatedAt=t});d.FinancialCorrections.Add(new FinancialCorrection{OriginalSaleId=sale.Id,CorrectedAmount=0,CreatedAt=t});
        var tie=new ManualDebt{CustomerName="Empate",Amount=80};var sale2=new Sale{Product="Pagamento",TotalValue=0,PaymentGroupId=tie.Id};session.Sales.Add(sale2);
        d.FinancialCorrections.Add(new FinancialCorrection{OriginalSaleId=sale2.Id,CorrectedAmount=0,CreatedAt=t});d.FinancialCorrections.Add(new FinancialCorrection{OriginalSaleId=sale2.Id,CorrectedAmount=10,CreatedAt=t});
        var cancelled=new ManualDebt{CustomerName="Cancelada",Amount=30,CancelledAt=t};
        var partial=new ManualDebt{CustomerName="Parcial",Amount=90};var p1=new Sale{Product="Pagamento",TotalValue=25.5m};var p2=new Sale{Product="Pagamento",TotalValue=10.10m};session.Sales.Add(p1);session.Sales.Add(p2);partial.SaleIds.AddRange([p1.Id,p2.Id,p1.Id]);
        var duplicated=new ManualDebt{CustomerName="Duplicada",Amount=40};var dup=new Sale{Product="Pagamento",TotalValue=15};session.Sales.Add(dup);var other=new CashSession{OpeningAmount=0,OpenedBy="Teste"};other.Sales.Add(new Sale{Id=dup.Id,Product="Pagamento",TotalValue=15});d.Sessions.Add(other);duplicated.SaleIds.Add(dup.Id);
        d.ManualDebts.AddRange([reversed,tie,cancelled,partial,duplicated]);
        d.CollectionContacts.Add(new CollectionContact{DebtId=partial.Id,IsOrder=false,Status="Prometeu pagar",CreatedAt=t,Notes="a"});d.CollectionContacts.Add(new CollectionContact{DebtId=partial.Id,IsOrder=false,Status="Negociação",CreatedAt=t,Notes="b"});
        var b=new DebtBalances(d);
        Check(b.Closed(reversed)&&b.Remaining(reversed)==0,"estorno completo fecha a dívida");
        Check(!b.Closed(tie)&&b.Remaining(tie)==80,"empate de data: vale a última correção da lista (como OrderBy().LastOrDefault())");
        Check(b.Closed(cancelled),"cancelada fica fechada");
        Check(b.Paid(partial)==35.60m&&b.Remaining(partial)==54.40m&&b.Status(partial.Id)=="Prometeu pagar","parcial: venda repetida na lista conta uma vez; status do contato mais recente (empate: o primeiro)");
        Check(b.Paid(duplicated)==30,"mesma venda em dois caixas conta duas vezes (como a regra original)");
        Compare(d,"casos pontuais");
        Customers();
        Console.WriteLine($"DebtorsTests: {passed} aprovados");
    }
    // CustomerIndex.Find == ReceivableViews.Customer para Id, nome+telefone, homônimos, telefones repetidos e balcão.
    static void Customers()
    {
        var r=new Random(11);var data=new AppData();var names=new[]{"Ana","ana","Bruno","Carla","Balcão","Cliente balcão","Não informado","Érica","erica"};
        var phones=new[]{"11999990000","(11) 99999-0000","11 98888-7777","1198886888","","123","+55 11 97777-6666","5511977776666"};
        for(var i=0;i<400;i++)data.Customers.Add(new CustomerProfile{Name=names[r.Next(names.Length)],Contact=phones[r.Next(phones.Length)]});
        var index=new CustomerIndex(data);int compared=0;
        for(var i=0;i<3000;i++)
        {
            Guid? id=r.Next(4)==0?(r.Next(2)==0?data.Customers[r.Next(data.Customers.Count)].Id:Guid.NewGuid()):null;var name=names[r.Next(names.Length)];var contact=phones[r.Next(phones.Length)];
            if(index.Find(id,name,contact)?.Id!=ReceivableViews.Customer(data,id,name,contact)?.Id)throw new Exception($"FALHOU: cliente {name}/{contact}/{id} difere da regra original");
            compared++;
        }
        var unique=new CustomerProfile{Name="Único",Contact="11912345086"};data.Customers.Add(unique);index=new CustomerIndex(data);
        Check(index.Find(null,"único","(11) 91234-5086")?.Id==unique.Id&&ReceivableViews.Customer(data,null,"único","(11) 91234-5086")?.Id==unique.Id,"nome sem diferenciar maiúsculas + telefone normalizado acha o cliente");
        Check(compared==3000,"3000 buscas de cliente iguais à regra original");
    }
    static void Compare(AppData data,string label)
    {
        var b=new DebtBalances(data);int compared=0;
        foreach(var debt in data.ManualDebts)
        {
            var same=b.Paid(debt)==debt.Paid(data)&&b.Closed(debt)==debt.IsClosedByCancellation(data)&&b.Remaining(debt)==debt.Remaining(data)
                &&b.Remaining(debt).ToString(System.Globalization.CultureInfo.InvariantCulture)==debt.Remaining(data).ToString(System.Globalization.CultureInfo.InvariantCulture)
                &&b.Status(debt.Id)==CollectionControl.Status(data,debt.Id,false);
            if(!same)throw new Exception($"FALHOU: dívida {debt.CustomerName} ({label}) difere da regra original");
            compared++;
        }
        Check(compared==data.ManualDebts.Count,$"{compared} dívidas iguais à regra original ({label})");
    }
    static AppData Fixture(Random r)
    {
        var data=new AppData();var sessions=Enumerable.Range(0,6).Select(_=>new CashSession{OpeningAmount=0,OpenedBy="Teste"}).ToList();data.Sessions.AddRange(sessions);
        var start=new DateTime(2025,1,1);var statuses=CollectionControl.Statuses;
        for(var i=0;i<300;i++)
        {
            var debt=new ManualDebt{CustomerName="Cliente "+i,Amount=Math.Round((decimal)r.NextDouble()*500,r.Next(0,3)),InitialCollectionStatus=statuses[r.Next(statuses.Length)]};
            if(r.Next(15)==0)debt.CancelledAt=start.AddDays(r.Next(300));
            var payments=r.Next(0,4);
            for(var k=0;k<payments;k++)
            {
                var sale=new Sale{Product="Pagamento",TotalValue=Math.Round((decimal)r.NextDouble()*debt.Amount/2,2),CreatedAt=start.AddDays(r.Next(300))};
                if(r.Next(3)==0)sale.PaymentGroupId=debt.Id;
                sessions[r.Next(sessions.Count)].Sales.Add(sale);if(r.Next(5)!=0)debt.SaleIds.Add(sale.Id);
                if(r.Next(4)==0){var when=start.AddDays(r.Next(5));data.FinancialCorrections.Add(new FinancialCorrection{OriginalSaleId=sale.Id,AdjustmentSaleId=r.Next(2)==0?Guid.NewGuid():sale.Id,CorrectedAmount=r.Next(2)==0?0:sale.Total,CreatedAt=when});if(r.Next(2)==0)data.FinancialCorrections.Add(new FinancialCorrection{OriginalSaleId=sale.Id,CorrectedAmount=r.Next(2)==0?0:1,CreatedAt=when});}
            }
            if(r.Next(4)==0)for(var k=0;k<r.Next(1,3);k++)data.CollectionContacts.Add(new CollectionContact{DebtId=debt.Id,IsOrder=false,Status=statuses[r.Next(statuses.Length)],CreatedAt=start.AddDays(r.Next(3)),Notes="n"});
            // Pagamento zerado num grupo (candidato a estorno antigo).
            if(r.Next(6)==0){var zero=new Sale{Product="Pagamento",TotalValue=0,PaymentGroupId=debt.Id};sessions[0].Sales.Add(zero);data.FinancialCorrections.Add(new FinancialCorrection{OriginalSaleId=zero.Id,CorrectedAmount=0,CreatedAt=start});}
            data.ManualDebts.Add(debt);
        }
        return data;
    }
}
