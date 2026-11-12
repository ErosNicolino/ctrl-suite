namespace GraficaCaixa;

// QR Code (ISO/IEC 18004) para os PDFs gerados pelo próprio ERP: modo byte (UTF-8), correção M, versões 1 a 10 (até 213 bytes).
// Sem dependência externa: o PDF desenha cada módulo como um quadrado (vetor), nítido em qualquer impressora.
public static class QrCode
{
    // Por versão (índice = versão): total de codewords, codewords de correção por bloco e quantidade de blocos (nível M).
    static readonly int[] Total={0,26,44,70,100,134,172,196,242,292,346};
    static readonly int[] EcPerBlock={0,10,16,26,18,24,16,18,22,22,26};
    static readonly int[] Blocks={0,1,1,1,2,2,4,4,4,5,5};
    static readonly int[][] Alignment=[[],[],[6,18],[6,22],[6,26],[6,30],[6,34],[6,22,38],[6,24,42],[6,26,46],[6,28,50]];

    /// <summary>Matriz [linha,coluna] (true = escuro) ou null se o texto não couber na versão 10.</summary>
    public static bool[,]? Encode(string text)
    {
        var data=System.Text.Encoding.UTF8.GetBytes(text??"");
        for(var version=1;version<=10;version++)
        {
            var dataCodewords=Total[version]-EcPerBlock[version]*Blocks[version];var countBits=version<10?8:16;
            if(4+countBits+data.Length*8>dataCodewords*8)continue;
            return Build(version,Codewords(data,version,dataCodewords,countBits));
        }
        return null;
    }

    static byte[] Codewords(byte[] data,int version,int dataCodewords,int countBits)
    {
        var bits=new List<bool>();void Put(int value,int length){for(var i=length-1;i>=0;i--)bits.Add((value>>i&1)!=0);}
        Put(0b0100,4);Put(data.Length,countBits);foreach(var b in data)Put(b,8);
        Put(0,Math.Min(4,dataCodewords*8-bits.Count));while(bits.Count%8!=0)bits.Add(false);
        for(var pad=0xEC;bits.Count<dataCodewords*8;pad^=0xEC^0x11)Put(pad,8);
        var bytes=new byte[dataCodewords];for(var i=0;i<bits.Count;i++)if(bits[i])bytes[i>>3]|=(byte)(0x80>>(i&7));
        // Blocos: os primeiros são curtos, os últimos têm um codeword de dados a mais; dados e correção são intercalados.
        int blocks=Blocks[version],ec=EcPerBlock[version],total=Total[version],shortCount=blocks-total%blocks,shortData=total/blocks-ec;
        var divisor=Generator(ec);var dataBlocks=new List<byte[]>();var ecBlocks=new List<byte[]>();
        for(int i=0,k=0;i<blocks;i++){var length=shortData+(i<shortCount?0:1);var block=bytes[k..(k+length)];k+=length;dataBlocks.Add(block);ecBlocks.Add(Remainder(block,divisor));}
        var result=new List<byte>(total);
        for(var i=0;i<=shortData;i++)foreach(var block in dataBlocks)if(i<block.Length)result.Add(block[i]);
        for(var i=0;i<ec;i++)foreach(var block in ecBlocks)result.Add(block[i]);
        return [..result];
    }

    // Reed–Solomon em GF(256), polinômio 0x11D.
    static byte Multiply(byte x,byte y){var z=0;for(var i=7;i>=0;i--){z=(z<<1)^((z>>7)*0x11D);z^=(y>>i&1)*x;}return (byte)z;}
    static byte[] Generator(int degree)
    {
        var result=new byte[degree];result[degree-1]=1;byte root=1;
        for(var i=0;i<degree;i++){for(var j=0;j<degree;j++){result[j]=Multiply(result[j],root);if(j+1<degree)result[j]^=result[j+1];}root=Multiply(root,0x02);}
        return result;
    }
    static byte[] Remainder(byte[] data,byte[] divisor)
    {
        var result=new byte[divisor.Length];
        foreach(var b in data){var factor=(byte)(b^result[0]);Array.Copy(result,1,result,0,result.Length-1);result[^1]=0;for(var i=0;i<result.Length;i++)result[i]^=Multiply(divisor[i],factor);}
        return result;
    }

    static bool[,] Build(int version,byte[] codewords)
    {
        var size=version*4+17;var modules=new bool[size,size];var function=new bool[size,size];
        void Set(int x,int y,bool dark){modules[y,x]=dark;function[y,x]=true;}
        for(var i=0;i<size;i++){Set(6,i,i%2==0);Set(i,6,i%2==0);}
        foreach(var (cx,cy) in new[]{(3,3),(size-4,3),(3,size-4)})
            for(var dy=-4;dy<=4;dy++)for(var dx=-4;dx<=4;dx++){int x=cx+dx,y=cy+dy;if(x<0||y<0||x>=size||y>=size)continue;var d=Math.Max(Math.Abs(dx),Math.Abs(dy));Set(x,y,d!=2&&d!=4);}
        var align=Alignment[version];
        for(var i=0;i<align.Length;i++)for(var j=0;j<align.Length;j++)
        {
            if(i==0&&j==0||i==0&&j==align.Length-1||i==align.Length-1&&j==0)continue;
            for(var dy=-2;dy<=2;dy++)for(var dx=-2;dx<=2;dx++)Set(align[i]+dx,align[j]+dy,Math.Max(Math.Abs(dx),Math.Abs(dy))!=1);
        }
        DrawFormat(0,size,Set);
        if(version>=7)
        {
            var rem=version;for(var i=0;i<12;i++)rem=(rem<<1)^((rem>>11)*0x1F25);var bits=version<<12|rem;
            for(var i=0;i<18;i++){var dark=(bits>>i&1)!=0;int a=size-11+i%3,b=i/3;Set(a,b,dark);Set(b,a,dark);}
        }
        // Dados em zigue-zague de baixo para cima, de duas em duas colunas, pulando a coluna de tempo (6).
        var bit=0;
        for(var right=size-1;right>=1;right-=2)
        {
            if(right==6)right=5;
            for(var vert=0;vert<size;vert++)for(var j=0;j<2;j++)
            {
                var x=right-j;var upward=((right+1)&2)==0;var y=upward?size-1-vert:vert;
                if(function[y,x]||bit>=codewords.Length*8)continue;
                modules[y,x]=(codewords[bit>>3]>>(7-(bit&7))&1)!=0;bit++;
            }
        }
        // Escolhe a máscara de menor penalidade (padrão da norma).
        bool[,]? best=null;var bestScore=int.MaxValue;
        for(var mask=0;mask<8;mask++)
        {
            var candidate=(bool[,])modules.Clone();
            for(var y=0;y<size;y++)for(var x=0;x<size;x++)if(!function[y,x]&&Masked(mask,x,y))candidate[y,x]=!candidate[y,x];
            DrawFormat(mask,size,(x,y,dark)=>candidate[y,x]=dark);
            var score=Penalty(candidate,size);if(score<bestScore){bestScore=score;best=candidate;}
        }
        return best!;
    }

    static bool Masked(int mask,int x,int y)=>mask switch
    {
        0=>(x+y)%2==0,1=>y%2==0,2=>x%3==0,3=>(x+y)%3==0,4=>(x/3+y/2)%2==0,5=>x*y%2+x*y%3==0,6=>(x*y%2+x*y%3)%2==0,_=>((x+y)%2+x*y%3)%2==0
    };

    // Formato: nível M (bits 00) + máscara, BCH(15,5) e XOR 0x5412; duas cópias e o módulo escuro fixo.
    static void DrawFormat(int mask,int size,Action<int,int,bool> set)
    {
        var data=mask;var rem=data;for(var i=0;i<10;i++)rem=(rem<<1)^((rem>>9)*0x537);var bits=(data<<10|rem)^0x5412;
        bool Bit(int i)=>(bits>>i&1)!=0;
        for(var i=0;i<=5;i++)set(8,i,Bit(i));
        set(8,7,Bit(6));set(8,8,Bit(7));set(7,8,Bit(8));
        for(var i=9;i<15;i++)set(14-i,8,Bit(i));
        for(var i=0;i<8;i++)set(size-1-i,8,Bit(i));
        for(var i=8;i<15;i++)set(8,size-15+i,Bit(i));
        set(8,size-8,true);
    }

    static int Penalty(bool[,] m,int size)
    {
        var score=0;var dark=0;
        for(var pass=0;pass<2;pass++)
            for(var a=0;a<size;a++)
            {
                var run=1;var line=new bool[size];
                for(var b=0;b<size;b++)line[b]=pass==0?m[a,b]:m[b,a];
                for(var b=1;b<=size;b++)
                {
                    if(b<size&&line[b]==line[b-1]){run++;continue;}
                    if(run>=5)score+=3+run-5;run=1;
                }
                // Padrão parecido com o de localização (1:1:3:1:1 com 4 claros de um lado).
                for(var b=0;b+10<size;b++)
                {
                    bool P(int k)=>line[b+k];
                    var core=P(4)&&!P(5)&&P(6)&&P(7)&&P(8)&&!P(9)&&P(10);
                    if(core&&!P(0)&&!P(1)&&!P(2)&&!P(3))score+=40;
                    var core2=P(0)&&!P(1)&&P(2)&&P(3)&&P(4)&&!P(5)&&P(6);
                    if(core2&&!P(7)&&!P(8)&&!P(9)&&!P(10))score+=40;
                }
            }
        for(var y=0;y<size;y++)for(var x=0;x<size;x++)
        {
            if(m[y,x])dark++;
            if(x+1<size&&y+1<size&&m[y,x]==m[y,x+1]&&m[y,x]==m[y+1,x]&&m[y,x]==m[y+1,x+1])score+=3;
        }
        var total=size*size;var k=(Math.Abs(dark*20-total*10)+total-1)/total-1;score+=Math.Max(0,k)*10;
        return score;
    }
}
