param([string]$Root = 'C:/workspace/2-PJT/S15P21B202/.local-worktrees/603')
Add-Type -AssemblyName System.Drawing
$assets = (Get-Content (Join-Path $Root 'docs/assets/concepts/stage2/model-references/asset-manifest.json') -Raw | ConvertFrom-Json).assets
$out = Join-Path $Root 'artifacts/stage2-models'
New-Item -ItemType Directory -Force $out | Out-Null
for ($page=0; $page -lt [math]::Ceiling($assets.Count/8); $page++) {
    $canvas = New-Object System.Drawing.Bitmap(1200,1920)
    $g=[System.Drawing.Graphics]::FromImage($canvas)
    $g.Clear([System.Drawing.Color]::White)
    $font=New-Object System.Drawing.Font('Arial',11)
    for ($row=0; $row -lt 8; $row++) {
        if ($page*8+$row -ge $assets.Count) { break }
        $asset=$assets[$page*8+$row]
        $views=@('front','left','right','back')
        for ($col=0; $col -lt 4; $col++) {
            $path=Join-Path $Root "docs/assets/concepts/stage2/model-references/$($asset.id)/$($views[$col]).png"
            $x=$col*300; $y=$row*240
            if(Test-Path $path){
                $im=[System.Drawing.Image]::FromFile($path)
                $scale=[math]::Min(290/$im.Width,214/$im.Height)
                $w=[int]($im.Width*$scale); $h=[int]($im.Height*$scale)
                $g.DrawImage($im,[int]($x+(300-$w)/2),[int]$y,$w,$h)
                $im.Dispose()
            }
            $g.DrawString("$($asset.id) / $($views[$col])",$font,[System.Drawing.Brushes]::Black,[float]($x+4),[float]($y+219))
        }
    }
    $canvas.Save((Join-Path $out "reference-review-$page.jpg"),[System.Drawing.Imaging.ImageFormat]::Jpeg)
    $g.Dispose();$canvas.Dispose();$font.Dispose()
}
