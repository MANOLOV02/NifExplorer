Option Strict On

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>Paleta y estilos del explorador. Un solo lugar para que la UI se vea de una pieza.
''' <para>⛔ Los nombres NO son <c>Panel</c>, <c>Texto</c> ni <c>Fuente</c> a propósito: los miembros de
''' un <c>Module</c> público de VB son visibles SIN calificar en todo el ensamblado, VB no distingue
''' mayúsculas, y esos tres chocaban con <c>Windows.Forms.Panel</c>, con parámetros llamados
''' <c>texto</c> y con la clase <see cref="Fuente"/> de este mismo proyecto.</para></summary>
Public Module Tema

    Public ReadOnly ColFondo As Color = Color.FromArgb(28, 28, 30)
    Public ReadOnly ColSuperficie As Color = Color.FromArgb(38, 38, 42)
    Public ReadOnly ColSuperficieAlta As Color = Color.FromArgb(48, 48, 53)
    Public ReadOnly ColBorde As Color = Color.FromArgb(58, 58, 64)
    Public ReadOnly ColFrente As Color = Color.FromArgb(226, 226, 230)
    Public ReadOnly ColFrenteTenue As Color = Color.FromArgb(146, 146, 154)
    Public ReadOnly ColAcento As Color = Color.FromArgb(0, 152, 214)
    Public ReadOnly ColAlerta As Color = Color.FromArgb(232, 152, 58)
    Public ReadOnly ColRojo As Color = Color.FromArgb(226, 92, 92)

    Public ReadOnly Letra As New Font("Segoe UI", 9.0F)
    Public ReadOnly LetraTitulo As New Font("Segoe UI Semibold", 11.0F)

    ''' <summary>Botón plano del tema.</summary>
    Public Function Boton(rotulo As String, ancho As Integer) As Button
        Dim b As New Button With {
            .Text = rotulo,
            .FlatStyle = FlatStyle.Flat,
            .BackColor = ColSuperficieAlta,
            .ForeColor = ColFrente,
            .Font = Letra,
            .Height = 26,
            .Width = If(ancho > 0, ancho, 80),
            .AutoSize = False,
            .Margin = New Padding(0, 0, 6, 0),
            .UseVisualStyleBackColor = False,
            .Cursor = Cursors.Hand
        }
        b.FlatAppearance.BorderColor = ColBorde
        b.FlatAppearance.BorderSize = 1
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(62, 62, 68)
        b.FlatAppearance.MouseDownBackColor = ColAcento
        Return b
    End Function

    ''' <summary>Marca un botón como "seleccionado" en el par segmentado del juego activo.</summary>
    Public Sub MarcarActivo(b As Button, activo As Boolean)
        If activo Then
            b.BackColor = ColAcento
            b.ForeColor = Color.White
            b.FlatAppearance.BorderColor = ColAcento
        Else
            b.BackColor = ColSuperficieAlta
            b.ForeColor = ColFrenteTenue
            b.FlatAppearance.BorderColor = ColBorde
        End If
    End Sub
End Module
