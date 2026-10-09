using Buddy.Browser;
using System.Buffers.Binary;
using System.Text;

int checks=0;
void Check(bool condition,string label){checks++;if(!condition)throw new InvalidOperationException(label);}
async Task Refuses(byte[] input,string label)
{
    try{using var s=new MemoryStream(input);await BrowserNativeTransport.ReadAsync(s,default);throw new InvalidOperationException("Accepted "+label);}
    catch(Exception e) when(e is InvalidDataException or EndOfStreamException){checks++;}
}
using(var empty=new MemoryStream())Check(await BrowserNativeTransport.ReadAsync(empty,default) is null,"clean EOF");
await Refuses([1],"partial header");await Refuses([0,0,0,0],"zero frame");
byte[] oversize=new byte[4];BinaryPrimitives.WriteUInt32LittleEndian(oversize,65537);await Refuses(oversize,"oversized frame before allocation");
await Refuses([255,255,255,255],"unsigned overflow");await Refuses([3,0,0,0,10],"partial payload");
byte[] first=Encoding.UTF8.GetBytes("{\"kind\":\"readiness.report\",\"text\":\"汉字😀\"}"),second=Enumerable.Range(0,65536).Select(i=>(byte)i).ToArray();
using(var wire=new MemoryStream()){
    await BrowserNativeTransport.WriteAsync(wire,first,default);await BrowserNativeTransport.WriteAsync(wire,second,default);wire.Position=0;
    using var split=new FragmentedStream(wire);
    Check((await BrowserNativeTransport.ReadAsync(split,default))!.SequenceEqual(first),"fragmented UTF8 bytes intact");
    Check((await BrowserNativeTransport.ReadAsync(split,default))!.SequenceEqual(second),"maximum binary frame intact");
    Check(await BrowserNativeTransport.ReadAsync(split,default) is null,"adjacent frames end exactly");
}
foreach(var invalid in new[]{Array.Empty<byte>(),new byte[65537]}){
    using var wire=new MemoryStream();try{await BrowserNativeTransport.WriteAsync(wire,invalid,default);throw new Exception("invalid writer accepted");}catch(InvalidDataException){Check(wire.Length==0,"no partial invalid output");}
}
using(var cancelled=new CancellationTokenSource()){
    cancelled.Cancel();using var wire=new MemoryStream(first);
    try{await BrowserNativeTransport.ReadAsync(wire,cancelled.Token);throw new Exception("cancel ignored");}catch(OperationCanceledException){checks++;}
}
Console.WriteLine($"PASS: {checks} bounded framing checks; owned memory only, no browser or account.");
Console.WriteLine($"PASS: {SelectedContextChecks.Run()} exact selected browser-context checks; owned memory only.");
if(args.Length==3&&args[0]=="--owned-native-host")await NativeHostChecks.Run(args[1],args[2]);
else if(args.Length!=0)throw new ArgumentException("Unsupported fixture arguments.");

sealed class FragmentedStream(Stream inner):Stream
{
    public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
    public override int Read(byte[] buffer,int offset,int count)=>inner.Read(buffer,offset,Math.Min(count,3));
    public override ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken ct=default)=>inner.ReadAsync(buffer[..Math.Min(buffer.Length,3)],ct);
    public override void Flush()=>throw new NotSupportedException();public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long value)=>throw new NotSupportedException();public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
}
