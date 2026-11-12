using GraficaCaixa;

// QR da ficha (2026.10.23.2): gera matrizes de tamanhos variados (versões 1 a 10) e grava em JSON para conferir com um leitor externo.
public static class QrTests
{
    public static void Run(string output)
    {
        var samples=new[]{"https://pc1:5444/app/#pedidos/109a1b84-97aa-427c-9bbc-7a6b085b0a75","OLÁ","1",new string('a',40),new string('7',100),new string('z',150),new string('x',213),"Pedido #55 · Beto · São Paulo"};
        var lines=new List<string>();
        foreach(var text in samples)
        {
            var m=QrCode.Encode(text)??throw new Exception("FALHOU: não coube "+text.Length);
            var size=m.GetLength(0);var rows=Enumerable.Range(0,size).Select(y=>new string(Enumerable.Range(0,size).Select(x=>m[y,x]?'1':'0').ToArray()));
            lines.Add(System.Text.Json.JsonSerializer.Serialize(new{text,size,rows}));
            Console.WriteLine($"OK {text.Length} bytes → {size}×{size} (versão {(size-17)/4})");
        }
        if(QrCode.Encode(new string('x',214))!=null)throw new Exception("FALHOU: 214 bytes deveria passar da versão 10");
        File.WriteAllText(output,"["+string.Join(",",lines)+"]");
        Console.WriteLine("QR PASSED (decodificar com leitor externo)");
    }
}
