Option Strict On

Imports System.IO

''' <summary>
''' Extrae a disco el NIF abierto y todo lo que necesita para volver a verse en otra parte,
''' <b>respetando los paths relativos del juego</b>: el .nif bajo <c>Meshes\…</c>, las texturas bajo
''' <c>Textures\…</c> y los materiales bajo <c>Materials\…</c>. Así la carpeta destino es un Data válido:
''' se la agrega como fuente (o se la empaqueta) y anda tal cual.
''' </summary>
Public Module Exportador

    Public Class Resultado
        Public Property Escritos As New List(Of String)
        Public Property Fallados As New List(Of String)
        Public Property Destino As String = ""
    End Class

    ''' <summary>
    ''' </summary>
    ''' <param name="destino">Carpeta raíz. Se crea el árbol relativo adentro.</param>
    ''' <param name="rutaRelativaNif">Path relativo del NIF (p.ej. <c>Meshes\Armor\x.nif</c>). Si el NIF
    ''' vino de un archivo suelto de afuera de toda fuente, alcanza con su nombre.</param>
    ''' <param name="bytesNif">Bytes del NIF tal como se abrieron. No se re-serializa el NIF: lo que se
    ''' copia es el archivo ORIGINAL, byte a byte. Un visor no tiene por qué reescribir mallas ajenas.</param>
    ''' <param name="dependencias">Paths relativos ya resueltos (texturas y materiales).</param>
    Public Function Exportar(destino As String,
                             rutaRelativaNif As String,
                             bytesNif As Byte(),
                             dependencias As IEnumerable(Of String),
                             resolutor As Resolutor) As Resultado

        Dim r As New Resultado With {.Destino = destino}

        Dim relNif = Resolutor.Normalizar(rutaRelativaNif)
        If relNif = "" Then relNif = "salida.nif"
        ' Un path relativo con ".." o raíz absoluta escribiría FUERA de la carpeta elegida. Se sanea.
        relNif = Sanear(relNif)

        If bytesNif IsNot Nothing AndAlso bytesNif.Length > 0 Then
            If Escribir(Path.Combine(destino, relNif), bytesNif) Then
                r.Escritos.Add(relNif)
            Else
                r.Fallados.Add(relNif)
            End If
        End If

        Dim vistos As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For Each dep In dependencias
            Dim rel = Sanear(Resolutor.Normalizar(dep))
            If rel = "" OrElse Not vistos.Add(rel) Then Continue For

            Dim datos = resolutor.Bytes(rel)
            If datos Is Nothing OrElse datos.Length = 0 Then
                r.Fallados.Add(rel)
                Continue For
            End If

            If Escribir(Path.Combine(destino, rel), datos) Then
                r.Escritos.Add(rel)
            Else
                r.Fallados.Add(rel)
            End If
        Next

        Return r
    End Function

    ''' <summary>Deja el path RELATIVO y adentro de la carpeta destino: sin unidad, sin raíz y sin
    ''' segmentos <c>..</c>. Un path de un NIF ajeno es dato no confiable; escribir donde él diga sería
    ''' dejar que un mod cualquiera elija carpeta de destino.</summary>
    Private Function Sanear(rel As String) As String
        If String.IsNullOrWhiteSpace(rel) Then Return ""
        Dim s = rel.Replace("/"c, "\"c).Trim()
        If s.Length >= 2 AndAlso s(1) = ":"c Then s = s.Substring(2)
        Dim partes = s.Split("\"c).
                       Where(Function(p) p <> "" AndAlso p <> "." AndAlso p <> "..").
                       Select(Function(p) LimpiarSegmento(p)).
                       Where(Function(p) p <> "").
                       ToArray()
        If partes.Length = 0 Then Return ""
        Return String.Join("\", partes)
    End Function

    Private Function LimpiarSegmento(seg As String) As String
        Dim malos = Path.GetInvalidFileNameChars()
        Dim sb As New Text.StringBuilder(seg.Length)
        For Each c In seg
            If Array.IndexOf(malos, c) < 0 Then sb.Append(c)
        Next
        Return sb.ToString().TrimEnd(" "c, "."c)
    End Function

    Private Function Escribir(rutaCompleta As String, datos As Byte()) As Boolean
        Try
            Dim dir = Path.GetDirectoryName(rutaCompleta)
            If Not String.IsNullOrEmpty(dir) Then Directory.CreateDirectory(dir)
            File.WriteAllBytes(rutaCompleta, datos)
            Return True
        Catch
            Return False
        End Try
    End Function
End Module
