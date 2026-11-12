using GraficaCaixa;

// Custo no orçamento (2026.10.22.8): área × R$/m² ÷ multiplicador, nunca abaixo do custo mínimo; retirada e arte não são custo.
public static class BudgetCostTests
{
    static int passed;
    static void Check(bool ok,string name){if(!ok)throw new Exception("FALHOU: "+name);Console.WriteLine("OK "+name);passed++;}
    public static void Run()
    {
        var rule=new BudgetRule{Product="Adesivo",SquareMeterRate=98,MinimumCost=27,MarkupMultiplier=2,NoArtworkFee=25,ArtworkFee=50};
        Check(rule.PriceBySize(1.2m,1.2m)==216.12m&&rule.Recommended(1.2m,1.2m)==216m,"valor para orçar: R$ 216,12 vira R$ 216,00");
        Check(rule.Recommended(1.5m,1.5m)==296m,"valor para orçar: R$ 295,50 sobe para R$ 296,00");
        Check(rule.Recommended(1.21m,1.2m)==217m,"valor para orçar: R$ 217,30 desce para R$ 217,00");
        Check(rule.Recommended(0.1m,0.1m)==129m,"valor para orçar: tamanho pequeno vale a venda mínima (R$ 129,00)");
        Check(rule.Recommended(1.2m,1.2m,3)==Math.Round(1.2m*1.2m*3*98m+75m,0,MidpointRounding.AwayFromZero),"valor para orçar com quantidade também arredonda no total");
        Check(rule.CostBySize(0.76m,0.65m)==0.76m*0.65m*49m,"custo pelo tamanho = área × R$/m² ÷ multiplicador");
        Check(rule.Cost(0.76m,0.65m)==27m,"tamanho pequeno: vale o custo mínimo (R$ 27,00)");
        Check(rule.Cost(1m,1m)==49m,"1 m²: R$ 49,00 (retirada e arte fora do custo)");
        Check(rule.Cost(2m,1.5m,2)==294m,"6 m² (2 unidades): R$ 294,00");
        Check(rule.Cost(0.333m,0.333m,7)==Math.Round(0.333m*0.333m*7*49m,2,MidpointRounding.AwayFromZero),"arredonda em 2 casas");
        rule.MarkupMultiplier=2.5m;Check(rule.Cost(1m,1m)==39.2m,"segue o multiplicador do produto (2,5)");
        rule.MarkupMultiplier=0;Check(rule.Cost(1m,1m)==27m,"multiplicador zerado não divide por zero: vale o mínimo");
        // Cores da margem (2026.10.23.1): verde a partir de 50%, âmbar a partir de 30%, vermelho abaixo; limites validados.
        var rules=new BusinessRules();
        Check(rules.MarginTone(50)=="COk"&&rules.MarginTone(49.9m)=="CWarn"&&rules.MarginTone(30)=="CWarn"&&rules.MarginTone(29.9m)=="CDanger","margem: 50% verde, 30% âmbar, abaixo vermelho");
        rules.MarginLow=60;var refused=false;try{rules.Validate();}catch(InvalidOperationException){refused=true;}Check(refused,"margem: âmbar acima do verde é recusado");
        // Preço por tipo de cliente (2026.10.23.2): desconto das Regras sobre o preço do produto; Balcão sem desconto; limite 0–90%.
        var price=new BusinessRules{ResellerDiscount=20,CompanyDiscount=10};
        Check(price.DiscountFor("Revenda")==20&&price.DiscountFor("Empresa")==10&&price.DiscountFor("")==0&&price.DiscountFor(null)==0,"tipo de cliente: Revenda 20%, Empresa 10%, Balcão 0");
        Check(BusinessRules.ApplyDiscount(92m,20)==73.6m&&BusinessRules.ApplyDiscount(0.45m,20)==0.36m&&BusinessRules.ApplyDiscount(10m,0)==10m,"desconto aplicado no preço unitário");
        price.ResellerDiscount=95;refused=false;try{price.Validate();}catch(InvalidOperationException){refused=true;}Check(refused,"desconto acima de 90% é recusado");
        // O tipo de preço é gravado no banco (coluna price_type) e volta na leitura.
        var folder=Path.Combine(Path.GetTempPath(),"cacp-pricetype-"+Guid.NewGuid().ToString("N"));
        try{var store=new DataStore(folder);store.EnsureInitialized();store.Update(d=>{d.Customers.Add(new CustomerProfile{Name="Gráfica Parceira",Contact="11999998888",PriceType="Revenda"});return 0;});
            Check(new DataStore(folder).Load().Customers.Single(x=>x.Name=="Gráfica Parceira").PriceType=="Revenda","tipo de preço gravado e relido do banco");}
        finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();try{Directory.Delete(folder,true);}catch(IOException){}}
        Console.WriteLine($"BUDGET COST PASSED: {passed}");
    }
}
