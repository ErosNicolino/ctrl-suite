using GraficaCaixa;
using GraficaCaixa.Wpf;
using System.Text;

// Conciliação Pix: leitura do OFX (SGML em Windows-1252 e XML em UTF-8) e cruzamento com os lançamentos PIX.
public static class ReconciliationTests
{
    static int passed;
    static void Check(bool ok,string name){if(!ok)throw new Exception("FALHOU: "+name);Console.WriteLine("OK "+name);passed++;}
    public static void Run()
    {
        var folder=Path.Combine(Path.GetTempPath(),"conciliacao-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            static string Trn(string type,string date,string amount,string id,string memo)=>$"<STMTTRN>\r\n<TRNTYPE>{type}\r\n<DTPOSTED>{date}120000[-3:BRT]\r\n<TRNAMT>{amount}\r\n<FITID>{id}\r\n<MEMO>{memo}\r\n</STMTTRN>\r\n";
            var sgml="OFXHEADER:100\r\nDATA:OFXSGML\r\nCHARSET:1252\r\n\r\n<OFX><BANKMSGSRSV1><STMTTRNRS><STMTRS><BANKTRANLIST>\r\n"
                +Trn("CREDIT","20261013","150.00","1","PIX RECEBIDO - JOÃO SILVA")
                +Trn("CREDIT","20261013","150.00","2","PIX RECEBIDO - MARIA SOUZA")
                +Trn("CREDIT","20261014","80,50","3","PIX RECEBIDO - ZILDA")
                +Trn("CREDIT","20261014","999.00","4","PIX RECEBIDO - DESCONHECIDO")
                +Trn("CREDIT","20261014","1234.56","5","GETNET SANTANDER VENDAS CREDITO")
                +Trn("DEBIT","20261014","-50.00","6","PIX ENVIADO - FORNECEDOR")
                +Trn("CREDIT","20261014","300.00","7","TED RECEBIDA CLIENTE")
                +"</BANKTRANLIST></STMTRS></STMTTRNRS></BANKMSGSRSV1></OFX>";
            var sgmlPath=Path.Combine(folder,"extrato.ofx");File.WriteAllBytes(sgmlPath,Encoding.Latin1.GetBytes(sgml));
            var bank=BankReconciliation.ReadOfx(sgmlPath);
            Check(bank.Count==7,"OFX SGML: sete lançamentos lidos");
            Check(bank[0].Description.Contains("JOÃO")&&bank[0].Date==new DateTime(2026,10,13,12,0,0),"OFX em Windows-1252 mantém acentos e hora");
            Check(bank[2].Amount==80.50m&&bank[5].Amount==-50m,"valor com vírgula e débito negativo");
            Check(bank[4].IsCardSettlement&&!bank[4].IsPix&&bank[0].IsPix,"repasse da Getnet separado do Pix");

            var xml="<?xml version=\"1.0\" encoding=\"UTF-8\"?><?OFX OFXHEADER=\"200\"?><OFX><BANKTRANLIST><STMTTRN><TRNTYPE>CREDIT</TRNTYPE><DTPOSTED>20261015</DTPOSTED><TRNAMT>12.30</TRNAMT><FITID>x</FITID><NAME>Pix recebido</NAME><MEMO>José Ávila</MEMO></STMTTRN></BANKTRANLIST></OFX>";
            var xmlPath=Path.Combine(folder,"extrato2.ofx");File.WriteAllText(xmlPath,xml,new UTF8Encoding(false));
            var xmlEntries=BankReconciliation.ReadOfx(xmlPath);
            Check(xmlEntries.Count==1&&xmlEntries[0].Amount==12.30m&&xmlEntries[0].Description.Contains("José Ávila")&&xmlEntries[0].IsPix,"OFX XML em UTF-8");
            var bad=Path.Combine(folder,"nao.ofx");File.WriteAllText(bad,"isto não é um extrato");
            var rejected=false;try{BankReconciliation.ReadOfx(bad);}catch(InvalidDataException){rejected=true;}
            Check(rejected,"arquivo que não é OFX é recusado com mensagem");

            Sale S(DateTime at,decimal total,string customer,string type="PIX",string product="Impressão")=>new(){CreatedAt=at,TotalValue=total,CustomerName=customer,PaymentType=type,Product=product,Units=1};
            var maria=S(new(2026,10,13,10,0,0),150,"Maria Souza");var joao=S(new(2026,10,13,11,0,0),150,"João Silva");
            var zilda=S(new(2026,10,13,17,0,0),80.50m,"Zilda");var lost=S(new(2026,10,14,9,0,0),45,"Sem Pix");
            var card=S(new(2026,10,14,9,0,0),300,"Cartão","Cartão de débito");
            var cancelled=S(new(2026,10,14,10,0,0),70,"Cancelado");var cancelAdjustment=S(new(2026,10,14,10,5,0),-70,"Cancelado",product:$"Ajuste do lançamento {cancelled.Id:N}: digitado errado");
            var erp=BankReconciliation.FromSales([maria,joao,zilda,lost,card,cancelled,cancelAdjustment]);
            Check(erp.Count==4&&erp.All(x=>x.Amount>0),"só PIX entram; lançamento zerado por ajuste fica de fora");

            var result=BankReconciliation.Reconcile(erp,bank,onlyPixCredits:true);
            Check(result.Rows.Single(x=>x.Erp?.Customer=="Maria Souza").Bank?.Id=="2"&&result.Rows.Single(x=>x.Erp?.Customer=="João Silva").Bank?.Id=="1","mesmo valor no mesmo dia: o nome do cliente decide o par");
            Check(result.Rows.Single(x=>x.Erp?.Customer=="Zilda").Status==BankReconciliation.OtherDay,"Pix que caiu no dia seguinte: conciliado em outro dia");
            Check(result.Rows.Single(x=>x.Erp?.Customer=="Sem Pix").Status==BankReconciliation.OnlyErp,"PIX lançado sem crédito no banco vira divergência");
            Check(result.OnlyBank.Single().Bank?.Id=="4","crédito Pix sem lançamento vira divergência");
            Check(result.CardSettlementCount==1&&result.CardSettlements==1234.56m&&result.IgnoredCredits==1,"repasse de cartão e TED ficam fora da conferência Pix");
            var all=BankReconciliation.Reconcile(erp,bank,onlyPixCredits:false);
            Check(all.OnlyBank.Count()==2,"desmarcando \"só PIX\", a TED entra na conferência");

            var twin=BankReconciliation.Reconcile([new(Guid.NewGuid(),new(2026,10,13,9,0,0),20,"A","x"),new(Guid.NewGuid(),new(2026,10,13,9,5,0),20,"B","x")],[new(new(2026,10,13),20,"PIX RECEBIDO","",""),new(new(2026,10,13),20,"PIX RECEBIDO","","")],true);
            Check(twin.Matched.Count()==2,"dois créditos idênticos no extrato casam com dois lançamentos");
            Console.WriteLine($"CONCILIAÇÃO: {passed} testes aprovados");
        }
        finally{try{Directory.Delete(folder,true);}catch(IOException){}}
    }
}
