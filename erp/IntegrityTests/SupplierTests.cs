using GraficaCaixa;
using System.Text.Json;

// Fornecedores: cadastro validado, ligação da compra pelo Id (lista) ou pelo nome (digitado), preços, comparação e gasto; servidor exige permissão de Compras.
public static class SupplierTests
{
    static int passed;
    static void Check(bool ok,string name){if(!ok)throw new Exception("FALHOU: "+name);Console.WriteLine("OK "+name);passed++;}
    static bool Throws(Action action){try{action();return false;}catch(InvalidOperationException){return true;}}
    static PurchaseNeed Need(string material,string unit="pct")=>new(){Material=material,Unit=unit,Quantity=1,RequestedBy="Teste",Status=PurchaseNeedStatus.Received};
    public static void Run()
    {
        var state=new Prompt12State();
        var lavoro=new Supplier{Name="Lavoro Papéis",Supplies=["Papel"," papel ","Adesivo",""],WhatsApp="11988887193"};
        var save=SupplierBook.Save(state,lavoro);
        Check(state.Suppliers.Count==1&&save.Before==""&&lavoro.Supplies.SequenceEqual(new[]{"Papel","Adesivo"}),"cadastro: grava e limpa a lista do que fornece (sem repetidos nem vazios)");
        Check(Throws(()=>SupplierBook.Save(state,new Supplier{Name="  lavoro   papéis "})),"não deixa cadastrar dois fornecedores com o mesmo nome");
        Check(Throws(()=>SupplierBook.Save(state,new Supplier{Name="   "})),"nome é obrigatório");
        Check(Throws(()=>SupplierBook.Save(state,new Supplier{Name="X",MinimumOrder=-1})),"pedido mínimo negativo é recusado");
        var renamed=JsonSerializer.Deserialize<Supplier>(JsonSerializer.Serialize(lavoro))!;renamed.Name="Lavoro Papéis Ltda";var edit=SupplierBook.Save(state,renamed);
        Check(state.Suppliers.Count==1&&state.Suppliers[0].Name=="Lavoro Papéis Ltda"&&edit.Before.Length>0&&state.Suppliers[0].CreatedAt==lavoro.CreatedAt,"editar mantém o mesmo fornecedor (não duplica) e a data de cadastro");

        var couche1=Need("Couchê 300g");var couche2=Need("couchê  300G");var vinil=Need("Vinil branco","m");var avulso=Need("Couchê 300g");
        state.PurchaseNeeds.AddRange([couche1,couche2,vinil,avulso]);
        state.Purchases.Add(new Purchase{NeedId=couche1.Id,SupplierId=lavoro.Id,Supplier="Lavoro Papéis",Quantity=2,Amount=170,Date=new DateTime(2026,9,20)});
        state.Purchases.Add(new Purchase{NeedId=couche2.Id,Supplier="lavoro papéis ltda",Quantity=2,Amount=179.80m,Date=new DateTime(2026,10,14)});
        state.Purchases.Add(new Purchase{NeedId=vinil.Id,SupplierId=lavoro.Id,Supplier="",Quantity=5,Amount=62,Date=new DateTime(2026,10,9)});
        state.Purchases.Add(new Purchase{NeedId=avulso.Id,Supplier="Papelaria Sul",Quantity=1,Amount=94,Date=new DateTime(2026,9,10)});
        var supplier=state.Suppliers[0];
        Check(SupplierBook.Purchases(state,supplier).Count()==3,"histórico junta compras ligadas pelo Id e as digitadas com o mesmo nome (sem diferenciar maiúsculas/espaços)");
        Check(!SupplierBook.Purchases(state,supplier).Any(x=>x.Amount==94),"compra de outro fornecedor digitado não entra no histórico");
        var prices=SupplierBook.LastPrices(state,supplier);var couche=prices.Single(x=>SupplierBook.Key(x.Material)=="couchê 300g");
        Check(prices.Count==2&&couche.UnitPrice==89.90m&&couche.PreviousUnitPrice==85m,"últimos preços: preço unitário atual (R$ 89,90) e o anterior (R$ 85,00) para mostrar ▲");
        var compare=SupplierBook.PricesFor(state,"COUCHÊ 300G");
        Check(compare.Count==2&&compare[0].UnitPrice==89.90m&&compare[0].Supplier=="Lavoro Papéis Ltda"&&compare[1].Supplier=="Papelaria Sul","comparação: último preço do material em cada fornecedor, do mais barato ao mais caro, com o nome atual do cadastro");
        Check(SupplierBook.Spend(state,supplier,new DateTime(2026,10,1),new DateTime(2026,11,1))==241.80m,"gasto do mês por fornecedor (outubro: R$ 179,80 + R$ 62,00)");
        var report=SupplierBook.SpendBySupplier(state,new DateTime(2026,9,1),new DateTime(2026,11,1));
        Check(report.Count==2&&report[0].Supplier=="Lavoro Papéis Ltda"&&report[0].Amount==411.80m&&report[1].Supplier=="Papelaria Sul","relatório: gasto por fornecedor no período, nomes livres aparecem como foram escritos");
        Check(SupplierBook.Find(state,null,"  LAVORO papéis ltda")?.Id==supplier.Id&&SupplierBook.Find(state,null,"Outro")==null,"nome digitado igual ao cadastro liga ao fornecedor; nome novo fica sem ligação (não trava a compra)");

        // Servidor: alterar a lista de fornecedores exige as permissões de Compras.
        var before=new AppData{FinancialIntegrityVersion=1,OrderDetailsVersion=4};var after=JsonSerializer.Deserialize<AppData>(JsonSerializer.Serialize(before))!;
        SupplierBook.Save(after.Prompt12,new Supplier{Name="Sublimax"});
        var actor=new UserIdentity(Guid.NewGuid(),"Teste","teste","Vendedor",Guid.NewGuid());var all=Permissions.Catalog.ToDictionary(x=>x.Key,_=>true);
        bool Accepted(IReadOnlyDictionary<string,bool> p){try{ServerAuthorization.ValidateAndAudit(before,JsonSerializer.Deserialize<AppData>(JsonSerializer.Serialize(after))!,actor,p);return true;}catch(Exception ex) when(ex is InvalidOperationException or UnauthorizedAccessException){return false;}}
        Check(Accepted(all),"servidor aceita novo fornecedor com permissão de Compras");
        Check(!Accepted(all.ToDictionary(x=>x.Key,x=>x.Key!="purchases.edit"&&x.Value)),"servidor recusa novo fornecedor sem permissão de editar Compras");
        var json=JsonSerializer.Serialize(state);var back=JsonSerializer.Deserialize<Prompt12State>(json)!;
        Check(back.Suppliers.Single().Supplies.Count==2&&back.Purchases.Count(x=>x.SupplierId==supplier.Id)==2,"fornecedores e ligações sobrevivem à gravação (JSON)");
        Console.WriteLine($"FORNECEDORES: {passed} testes aprovados");
    }
}
