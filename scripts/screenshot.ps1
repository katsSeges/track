# Captures the bottom-right corner of the primary screen (where the tray popover opens) to a PNG.
# Used to verify the popover, model card and tray against the mockups.
param(
    [Parameter(Mandatory)] [string] $Path,
    [int] $Width = 720,
    [int] $Height = 720
)
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bitmap = New-Object System.Drawing.Bitmap $Width, $Height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.CopyFromScreen($screen.Right - $Width, $screen.Bottom - $Height, 0, 0, $bitmap.Size)
$bitmap.Save($Path)
$graphics.Dispose()
$bitmap.Dispose()
$Path
