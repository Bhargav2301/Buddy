using Buddy.Server;
using Microsoft.AspNetCore.DataProtection;
using System.IO;
using System.Net.Http;
using System.Text.Json;

// Separate service/vision acceptance. This deliberately does not claim live capture or native focus.
internal static class RegionalModelChecks
{
    internal static async Task<int> Run()
    {
        var folder=Path.Combine(Path.GetTempPath(),"Buddy-region-model-"+Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try {
            var store=new StateStore(folder,DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(folder,"keys"))));
            await store.Update(s=>{s.Model=s.VisionModel="gemma3:4b";return true;});
            using var bitmap=new System.Drawing.Bitmap(600,160);
            using(var g=System.Drawing.Graphics.FromImage(bitmap))
            using(var font=new System.Drawing.Font("Arial",36,System.Drawing.FontStyle.Regular,System.Drawing.GraphicsUnit.Pixel)) {
                g.Clear(System.Drawing.Color.White);
                g.FillRectangle(System.Drawing.Brushes.LightGray,20,20,560,120);
                g.DrawRectangle(System.Drawing.Pens.Black,20,20,560,120);
                g.DrawString("Selected control",font,System.Drawing.Brushes.Black,45,55);
            }
            using var image=new MemoryStream();bitmap.Save(image,System.Drawing.Imaging.ImageFormat.Png);
            var bytes=image.ToArray();
            try {
                using var http=new HttpClient(new Trace()) {BaseAddress=new("http://127.0.0.1:11434"),Timeout=TimeSpan.FromMinutes(3)};
                var service=new BuddyService(store,new(http));
                using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(3));
                // No UIA names: the label must come from the deliberately synthetic image.
                var turn=await service.Teach(new("Read the exact label visible in this selected area. Explain only what is visible; do not click or suggest an action.",new("synthetic","Synthetic selected area",[]),RegionImageBase64:Convert.ToBase64String(bytes)),deadline.Token);
                Console.WriteLine("SYNTHETIC REGIONAL MODEL: "+turn.Speech);
                if(!turn.Speech.Contains("Selected control",StringComparison.OrdinalIgnoreCase)||!ConversationalReply.IsConcise(turn.Speech))
                    throw new Exception("FAIL: Local vision did not read the synthetic regional image concisely.");
                Console.WriteLine("ALL 1 SYNTHETIC REGIONAL VISION CHECK PASSED (no native capture, screen focus or physical gesture acceptance)");
                return 0;
            } finally {Array.Clear(bytes);}
        } catch(Exception e){Console.Error.WriteLine(e);return 1;}
        finally {Directory.Delete(folder,true);}
    }
    private sealed class Trace():DelegatingHandler(new HttpClientHandler())
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var response=await base.SendAsync(request,ct);
            using var json=JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(ct));
            if(json.RootElement.TryGetProperty("message",out var message))Console.WriteLine("SYNTHETIC MODEL RAW: "+message.GetProperty("content").GetString());
            return response;
        }
    }
}
