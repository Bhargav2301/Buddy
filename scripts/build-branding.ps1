param()
$ErrorActionPreference='Stop'
$brand=Join-Path (Split-Path -Parent $PSScriptRoot) 'apps\windows\Buddy.Windows\Assets\Branding'
Add-Type -AssemblyName System.Drawing.Common
$source=[Drawing.Bitmap]::FromFile((Join-Path $brand 'Buddy.png'))
try {
    $frames=@()
    foreach($size in @(16,24,32,48,64,128,256)){
        $bitmap=[Drawing.Bitmap]::new($size,$size,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics=[Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode=[Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $scale=($size*0.92)/[Math]::Max($source.Width,$source.Height)
            $width=[single]($source.Width*$scale);$height=[single]($source.Height*$scale)
            $rectangle=[Drawing.RectangleF]::new([single](($size-$width)/2),[single](($size-$height)/2),$width,$height)
            $graphics.DrawImage($source,$rectangle)
            $memory=[IO.MemoryStream]::new()
            try {$bitmap.Save($memory,[Drawing.Imaging.ImageFormat]::Png);$frames+=@{size=$size;bytes=$memory.ToArray()}}finally{$memory.Dispose()}
        }finally{$graphics.Dispose();$bitmap.Dispose()}
    }
    $file=[IO.File]::Create((Join-Path $brand 'Buddy.ico'));$writer=[IO.BinaryWriter]::new($file)
    try {
        $writer.Write([UInt16]0);$writer.Write([UInt16]1);$writer.Write([UInt16]$frames.Count)
        $offset=6+16*$frames.Count
        foreach($frame in $frames){$dimension=if($frame.size -eq 256){0}else{$frame.size};$writer.Write([byte]$dimension);$writer.Write([byte]$dimension);$writer.Write([byte]0);$writer.Write([byte]0);$writer.Write([UInt16]1);$writer.Write([UInt16]32);$writer.Write([UInt32]$frame.bytes.Length);$writer.Write([UInt32]$offset);$offset+=$frame.bytes.Length}
        foreach($frame in $frames){$writer.Write([byte[]]$frame.bytes)}
    }finally{$writer.Dispose();$file.Dispose()}
}finally{$source.Dispose()}
Write-Output 'Packaged the supplied character into a 16-256px application icon. Artwork content is unchanged.'
