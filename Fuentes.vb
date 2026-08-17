Option Strict On

Imports System.IO
Imports System.Text.Json
Imports System.Text.Json.Serialization

''' <summary>
''' UNA fuente de datos del explorador: una carpeta suelta o un archive (.ba2 / .bsa).
''' <para>La lista ORDENADA de fuentes es a la vez de dónde se navegan los NIF y de dónde se
''' resuelven texturas y materiales: <b>gana la primera que tenga el path</b>. No hay orden de carga,
''' ni Plugins.txt, ni INIs, ni descubrimiento automático — el orden lo decide el usuario y lo ve.</para>
''' </summary>
Public Class Fuente

    Public Property Ruta As String = ""

    ''' <summary>Carpeta o archive. Se deriva de la ruta, no se persiste: si el usuario mueve un
    ''' archivo a una carpeta con el mismo nombre, mandar el valor viejo sería peor que recalcularlo.</summary>
    Public ReadOnly Property EsArchive As Boolean
        Get
            Dim ext = Path.GetExtension(Ruta)
            Return String.Equals(ext, ".ba2", StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(ext, ".bsa", StringComparison.OrdinalIgnoreCase)
        End Get
    End Property

    Public ReadOnly Property Nombre As String
        Get
            If EsArchive Then Return Path.GetFileName(Ruta)
            Dim n = Path.GetFileName(Ruta.TrimEnd(Path.DirectorySeparatorChar))
            Return If(n = "", Ruta, n)
        End Get
    End Property

    Public ReadOnly Property Existe As Boolean
        Get
            If String.IsNullOrWhiteSpace(Ruta) Then Return False
            Return If(EsArchive, File.Exists(Ruta), Directory.Exists(Ruta))
        End Get
    End Property

    Public Overrides Function ToString() As String
        Return Nombre
    End Function
End Class

''' <summary>
''' Las listas persistidas, UNA POR JUEGO.
''' <para>⛔ DOS LISTAS, NO UNA MEZCLADA, y no es una preferencia estética: FO4 y SSE comparten
''' nombres de path (<c>textures\actors\character\...</c> existe en los dos), así que una sola lista
''' haría sangrar texturas de un juego en un NIF del otro sin un solo error. El juego lo declara el
''' header del NIF y eso elige qué lista está activa.</para>
''' </summary>
Public Class ConfigFuentes

    <JsonPropertyName("fo4")>
    Public Property FO4 As New List(Of String)

    <JsonPropertyName("sse")>
    Public Property SSE As New List(Of String)

    <JsonPropertyName("ultimoDirectorioExport")>
    Public Property UltimoDirectorioExport As String = ""

    Private Shared ReadOnly Opciones As New JsonSerializerOptions With {
        .WriteIndented = True,
        .DefaultIgnoreCondition = JsonIgnoreCondition.Never
    }

    ''' <summary>Archivo propio, al lado del exe. NO se toca <c>config.json</c>, que es el de
    ''' <see cref="Config_App"/> (luces, sombras, ajustes de render) y lo maneja la librería.</summary>
    Public Shared ReadOnly Property RutaArchivo As String
        Get
            Return Path.Combine(Application.StartupPath, "nif_explorer.fuentes.json")
        End Get
    End Property

    Public Shared Function Cargar() As ConfigFuentes
        Try
            If Not File.Exists(RutaArchivo) Then Return New ConfigFuentes()
            Dim txt = File.ReadAllText(RutaArchivo)
            If String.IsNullOrWhiteSpace(txt) Then Return New ConfigFuentes()
            Dim c = JsonSerializer.Deserialize(Of ConfigFuentes)(txt, Opciones)
            If c Is Nothing Then Return New ConfigFuentes()
            If c.FO4 Is Nothing Then c.FO4 = New List(Of String)
            If c.SSE Is Nothing Then c.SSE = New List(Of String)
            Return c
        Catch
            ' Un JSON corrupto NO puede impedir abrir el programa: se arranca con listas vacías y el
            ' usuario re-arma las fuentes, que es un gesto de dos clicks.
            Return New ConfigFuentes()
        End Try
    End Function

    Public Sub Guardar()
        Try
            File.WriteAllText(RutaArchivo, JsonSerializer.Serialize(Me, Opciones))
        Catch
            ' Guardar la lista es una comodidad, no el trabajo: si el disco está de solo lectura, se
            ' pierde la lista al cerrar pero la sesión en curso sigue entera.
        End Try
    End Sub

    Public Function Lista(juego As Config_App.Game_Enum) As List(Of String)
        Dim l = If(juego = Config_App.Game_Enum.Fallout4, FO4, SSE)
        OrdenarSueltosPrimero(l)
        Return l
    End Function

    ''' <summary>Deja las CARPETAS antes que los ARCHIVES, conservando el orden relativo dentro de cada
    ''' grupo.
    ''' <para>⛔ NO es una preferencia estética: es la ley del motor —un archivo suelto le gana SIEMPRE al
    ''' mismo path dentro de un BA2/BSA— y también la de NifSkope y Outfit Studio. Sin esto, agregar la
    ''' carpeta de un mod DESPUÉS de los archives la dejaba abajo en la lista y se seguían viendo las
    ''' texturas y los materiales de la BA2, en silencio: el usuario ve su suelto en la lista y no pasa
    ''' nada. Que la regla viva acá —y no escondida en el resolutor— es lo que hace que los números que
    ''' se ven en la lista SEAN el orden de resolución.</para>
    ''' <para>Dentro de cada grupo el orden sigue siendo del usuario: mover con ▲▼ ordena entre carpetas
    ''' o entre archives. Un archive no puede treparse por encima de una carpeta, igual que en el juego.</para></summary>
    Public Shared Sub OrdenarSueltosPrimero(lista As List(Of String))
        If lista Is Nothing OrElse lista.Count < 2 Then Return
        Dim esArchive = Function(r As String) New Fuente With {.Ruta = r}.EsArchive
        If Not lista.Any(esArchive) Then Return
        Dim sueltos = lista.Where(Function(r) Not esArchive(r)).ToList()
        Dim archives = lista.Where(esArchive).ToList()
        lista.Clear()
        lista.AddRange(sueltos)
        lista.AddRange(archives)
    End Sub
End Class
