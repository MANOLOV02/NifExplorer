Option Strict On

Imports System.IO
Imports BSA_BA2_Library_DLL.BethesdaArchive.Core

''' <summary>
''' El resolvedor de assets del explorador. Traduce "necesito <c>Textures\foo_d.dds</c>" a una entrada
''' concreta del diccionario de la librería, recorriendo las fuentes del usuario EN ORDEN.
'''
''' <para><b>Por qué existe y por qué así.</b> Todo lo que el render lee (texturas y materiales) lo pide
''' al estático <c>FilesDictionary_class</c>: no hay costura para inyectar un resolvedor propio. Entonces
''' el visor no lo reemplaza — lo ALIMENTA, y sólo con los ~10 paths que el NIF abierto pide. Nunca se
''' llama a <c>RegisterArchive</c>, que montaría las decenas de miles de entradas del archive entero.</para>
'''
''' <para><b>El truco que hace convivir varias raíces.</b> <c>File_Location</c> compone
''' <c>Path.Combine(FO4Path, BA2File)</c> y <c>Path.Combine(FO4Path, FullPath)</c>, o sea que el
''' diccionario de la librería modela UNA sola raíz de datos. Como <c>Path.Combine</c> con el segundo
''' argumento ABSOLUTO devuelve el segundo, guardando rutas absolutas <c>FO4Path</c> deja de importar y
''' las fuentes de cualquier carpeta (y de los dos juegos) conviven. De regalo, la caché de bytes de la
''' librería —indexada por <c>FullPath</c>— queda ÚNICA POR FUENTE, así que el mismo path relativo en dos
''' fuentes distintas no se puede pisar.</para>
''' </summary>
Public Class Resolutor

    ''' <summary>Una fuente ya abierta y lista para responder. Para un archive incluye su índice
    ''' <c>path relativo → nº de entrada</c>; para una carpeta no hay índice ninguno: preguntar por un
    ''' archivo suelto es un <c>File.Exists</c> y enumerar la carpeta entera sería trabajo tirado.</summary>
    Public Class FuenteViva
        Public Property Fuente As Fuente
        Public Property Orden As Integer
        Public Property Existe As Boolean
        Public Property Problema As String = ""
        ''' <summary>Sólo archives. Primera aparición gana (un archive sano no repite paths).</summary>
        Public Property Indice As Dictionary(Of String, Integer)
        ''' <summary>Sólo archives: los .nif que contiene, ordenados. Es lo que dibuja el árbol.</summary>
        Public Property Nifs As List(Of String)
        Public Property Entradas As Integer
        ''' <summary>Sello de contenido para detectar que el archive cambió EN DISCO durante la sesión.
        ''' ⛔ NO es opcional: las <c>File_Location</c> que armo a mano llevan <c>ArchiveGen = 0</c> y el
        ''' sello de generación de la librería siempre las acepta, así que si alguien repaca el .ba2 (WM
        ''' Pack, el packer de FaceGen) los índices viejos devolverían LOS BYTES DE OTRA ENTRADA, sin
        ''' error. Se compara antes de servir y, si cambió, se re-indexa y se purga la caché.</summary>
        Public Property Tamano As Long
        Public Property Fecha As Date
    End Class

    Private ReadOnly _vivas As New List(Of FuenteViva)
    Private _juegoMontado As Config_App.Game_Enum? = Nothing

    ''' <summary>Lo que ya está publicado en el diccionario de la librería: clave relativa → destino.
    ''' Sirve para NO re-publicar lo mismo: <c>AddOrUpdateDictionaryEntry</c> APILA la entrada anterior en
    ''' la pila de overrides, así que re-registrar las mismas claves NIF tras NIF durante una sesión larga
    ''' de navegación sería una fuga silenciosa.</summary>
    Private ReadOnly _publicadas As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>Diagnóstico del último NIF cargado: qué resolvió y contra qué fuente, y qué faltó.
    ''' Es lo que hace que un render plano nunca sea un misterio.</summary>
    Public ReadOnly Property Resueltas As New List(Of (Clave As String, Fuente As String))
    Public ReadOnly Property NoResueltas As New List(Of String)

    Public ReadOnly Property Fuentes As IReadOnlyList(Of FuenteViva)
        Get
            Return _vivas
        End Get
    End Property

    Public ReadOnly Property JuegoMontado As Config_App.Game_Enum?
        Get
            Return _juegoMontado
        End Get
    End Property

    ''' <summary>Monta la lista de un juego. Barato: por cada archive se abre el reader UNA vez para
    ''' leerle la tabla de entradas y se cierra; el pool de la librería lo reabre cuando haga falta
    ''' extraer bytes. Medido sobre el corpus del usuario: ~10 ms por archive de texturas, ~100 ms por
    ''' uno de mallas (42 k entradas).</summary>
    Public Sub Montar(juego As Config_App.Game_Enum, rutas As IEnumerable(Of String), Optional progreso As Action(Of String) = Nothing)
        Limpiar()
        _juegoMontado = juego

        Dim orden = 0
        For Each ruta In rutas
            Dim f As New Fuente With {.Ruta = ruta}
            Dim v As New FuenteViva With {.Fuente = f, .Orden = orden, .Existe = f.Existe}
            orden += 1

            If Not v.Existe Then
                v.Problema = "no existe"
                _vivas.Add(v)
                Continue For
            End If

            If f.EsArchive Then
                progreso?.Invoke("Indexando " & f.Nombre & "…")
                Try
                    Dim fi As New FileInfo(f.Ruta)
                    v.Tamano = fi.Length
                    v.Fecha = fi.LastWriteTimeUtc
                    Indexar(v)
                Catch ex As Exception
                    ' Un archive ilegible (versión no soportada, truncado, en uso) NO puede tumbar el
                    ' montaje de los demás: se marca y se sigue. Es lo que NifSkope expone como
                    ' "ignore archive errors".
                    v.Problema = ex.Message
                    v.Indice = New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)
                    v.Nifs = New List(Of String)
                End Try
            End If

            _vivas.Add(v)
        Next
    End Sub

    Private Shared Sub Indexar(v As FuenteViva)
        Dim idx As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)
        Dim nifs As New List(Of String)
        Using fs As New FileStream(v.Fuente.Ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
            Using arc As New BethesdaReader(fs)
                Dim entradas = arc.EntriesFiles
                For i = 0 To entradas.Count - 1
                    Dim p = Normalizar(entradas(i).FullPath)
                    If p = "" Then Continue For
                    If Not idx.ContainsKey(p) Then idx(p) = i
                    If p.EndsWith(".nif", StringComparison.OrdinalIgnoreCase) Then nifs.Add(p)
                Next
                v.Entradas = entradas.Count
            End Using
        End Using
        nifs.Sort(StringComparer.OrdinalIgnoreCase)
        v.Indice = idx
        v.Nifs = nifs
    End Sub

    ''' <summary>Re-indexa las fuentes de archive cuyo tamaño o fecha cambió en disco. Ver el comentario
    ''' de <see cref="FuenteViva.Tamano"/>: sin esto se sirven bytes de otra entrada en silencio.</summary>
    Public Function RevalidarArchives() As Boolean
        Dim huboCambio = False
        For Each v In _vivas
            If Not v.Fuente.EsArchive OrElse Not v.Fuente.Existe Then Continue For
            Try
                Dim fi As New FileInfo(v.Fuente.Ruta)
                If fi.Length = v.Tamano AndAlso fi.LastWriteTimeUtc = v.Fecha Then Continue For
                v.Tamano = fi.Length
                v.Fecha = fi.LastWriteTimeUtc
                Indexar(v)
                huboCambio = True
            Catch ex As Exception
                v.Problema = ex.Message
            End Try
        Next
        If huboCambio Then
            ' El índice cambió ⇒ TODA entrada publicada puede apuntar a un nº de entrada viejo.
            DespublicarTodo()
            FilesDictionary_class.ClearBytesCache()
        End If
        Return huboCambio
    End Function

    ''' <summary>Normalización de clave: separadores a <c>\</c>, sin <c>.\</c> ni barra inicial. Es la
    ''' misma forma que usan las claves del diccionario de la librería.</summary>
    Public Shared Function Normalizar(p As String) As String
        If String.IsNullOrWhiteSpace(p) Then Return ""
        Dim s = p.Trim().Replace("/"c, "\"c)
        While s.StartsWith("\", StringComparison.Ordinal)
            s = s.Substring(1)
        End While
        Return s
    End Function

    ''' <summary>Ruta absoluta de un path relativo dentro de una fuente CARPETA, o <c>""</c> si no está.
    ''' <para>⛔ SON DOS FORMAS, y las dos son gestos legítimos del usuario:</para>
    ''' <list type="number">
    ''' <item>la fuente es una raíz que CONTIENE <c>Textures\</c>, <c>Meshes\</c>… (un Data, o la carpeta
    ''' de un mod) ⇒ <c>&lt;fuente&gt;\&lt;rel&gt;</c>;</item>
    ''' <item>la fuente ES la carpeta <c>Textures</c> (o <c>Meshes</c>, o <c>Materials</c>) ⇒ el primer
    ''' segmento del path pedido ya está en la ruta de la fuente y hay que SACARLO:
    ''' <c>&lt;fuente&gt;\&lt;rel sin su primer segmento&gt;</c>.</item></list>
    ''' <para>Sin el segundo caso, agregar <c>…\Data\Textures</c> como fuente no resolvía NADA: el join
    ''' daba <c>…\Data\Textures\Textures\…</c>, los sueltos nunca ganaban, y se veían las texturas del
    ''' BA2 de más abajo en la lista — sin un solo error, que es lo peor.</para></summary>
    Public Function RutaSueltaDe(v As FuenteViva, relativo As String) As String
        Dim rel = Normalizar(relativo)
        If rel = "" Then Return ""
        Dim baseDir = v.Fuente.Ruta

        ' Windows es case-insensitive, así que el casing del path que trae el NIF no importa.
        Dim p1 = Path.Combine(baseDir, rel)
        If File.Exists(p1) Then Return p1

        Dim i = rel.IndexOf("\"c)
        If i > 0 Then
            Dim primerSegmento = rel.Substring(0, i)
            Dim hoja = Path.GetFileName(baseDir.TrimEnd(Path.DirectorySeparatorChar))
            If String.Equals(primerSegmento, hoja, StringComparison.OrdinalIgnoreCase) Then
                Dim p2 = Path.Combine(baseDir, rel.Substring(i + 1))
                If File.Exists(p2) Then Return p2
            End If
        End If

        Return ""
    End Function

    ''' <summary>Busca un path relativo en las fuentes EN ORDEN y devuelve la primera que lo tenga.
    ''' <c>Nothing</c> si no está en ninguna. Para una fuente de archive deja el nº de entrada en
    ''' <paramref name="indiceEntrada"/>; para una carpeta, la ruta absoluta en
    ''' <paramref name="rutaSuelta"/> — resolverla dos veces sería arriesgarse a que las dos llamadas
    ''' no coincidan.</summary>
    Public Function Buscar(relativo As String, ByRef indiceEntrada As Integer, ByRef rutaSuelta As String) As FuenteViva
        indiceEntrada = -1
        rutaSuelta = ""
        Dim rel = Normalizar(relativo)
        If rel = "" Then Return Nothing

        For Each v In _vivas
            If Not v.Existe Then Continue For
            If v.Fuente.EsArchive Then
                Dim i As Integer
                If v.Indice IsNot Nothing AndAlso v.Indice.TryGetValue(rel, i) Then
                    indiceEntrada = i
                    Return v
                End If
            Else
                Dim abs = RutaSueltaDe(v, rel)
                If abs <> "" Then
                    rutaSuelta = abs
                    Return v
                End If
            End If
        Next
        Return Nothing
    End Function

    ''' <summary>Resuelve un path relativo y lo PUBLICA en el diccionario de la librería, para que el
    ''' render lo encuentre cuando lo pida. Devuelve False si ninguna fuente lo tiene.</summary>
    Public Function Publicar(relativo As String) As Boolean
        Dim rel = Normalizar(relativo)
        If rel = "" Then Return False

        Dim idx As Integer
        Dim rutaSuelta As String = ""
        Dim v = Buscar(rel, idx, rutaSuelta)
        If v Is Nothing Then
            If Not NoResueltas.Contains(rel, StringComparer.OrdinalIgnoreCase) Then NoResueltas.Add(rel)
            Return False
        End If

        Dim destino As String
        Dim loc As New FilesDictionary_class.File_Location()

        If v.Fuente.EsArchive Then
            Dim abs = Path.GetFullPath(v.Fuente.Ruta)
            loc.BA2File = abs                   ' ABSOLUTO: Path.Combine(FO4Path, abs) = abs
            loc.Index = idx
            ' FullPath es SÓLO la clave de la caché de bytes para una entrada de archive (el camino de
            ' suelto es el único que lo usa como ruta). Se le pone algo único por fuente para que dos
            ' fuentes con el mismo path relativo no compartan celda de caché.
            loc.FullPath = abs & "::" & rel
            destino = abs & "::" & idx.ToString()
        Else
            ' La ruta la trae Buscar: es la que REALMENTE existe, contemplando que la fuente pueda ser
            ' la carpeta Textures/Meshes/Materials en vez de la raíz que las contiene.
            Dim abs = Path.GetFullPath(rutaSuelta)
            loc.BA2File = ""
            loc.Index = -1
            loc.FullPath = abs                  ' ABSOLUTO: idem
            destino = abs
        End If

        loc.SourceOrder = Integer.MaxValue - v.Orden
        loc.ArchiveGen = 0                      ' ContentGenOf() de un archive nunca desmontado también es 0

        Dim yaDestino As String = Nothing
        If _publicadas.TryGetValue(rel, yaDestino) AndAlso String.Equals(yaDestino, destino, StringComparison.OrdinalIgnoreCase) Then
            ' Mismo destino que la última vez: no se toca el diccionario. Republicar apilaría la entrada
            ' anterior en la pila de overrides de la librería, NIF tras NIF, toda la sesión.
            Anotar(rel, v)
            Return True
        End If

        FilesDictionary_class.AddOrUpdateDictionaryEntry(rel, loc)
        _publicadas(rel) = destino
        Anotar(rel, v)
        Return True
    End Function

    Private Sub Anotar(rel As String, v As FuenteViva)
        Resueltas.Add((rel, v.Fuente.Nombre))
    End Sub

    Public Sub ReiniciarDiagnostico()
        Resueltas.Clear()
        NoResueltas.Clear()
    End Sub

    ''' <summary>Bytes de un path relativo, por el camino de la librería (pool de readers + caché).
    ''' Devuelve un array vacío si no se pudo.</summary>
    Public Function Bytes(relativo As String) As Byte()
        If Not Publicar(relativo) Then Return Array.Empty(Of Byte)()
        Return FilesDictionary_class.GetBytes(Normalizar(relativo))
    End Function

    ''' <summary>Bytes de un NIF concreto de una fuente concreta (el árbol sabe de cuál salió, así que
    ''' no se re-resuelve por la cadena: un NIF de la fuente 3 se abre de la fuente 3 aunque la 1 tenga
    ''' uno con el mismo path).</summary>
    Public Function BytesDe(v As FuenteViva, relativo As String) As Byte()
        Dim rel = Normalizar(relativo)
        If v.Fuente.EsArchive Then
            Dim i As Integer
            If v.Indice Is Nothing OrElse Not v.Indice.TryGetValue(rel, i) Then Return Array.Empty(Of Byte)()
            Using fs As New FileStream(v.Fuente.Ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
                Using arc As New BethesdaReader(fs)
                    Return arc.ExtractToMemory(i)
                End Using
            End Using
        Else
            Dim abs = RutaSueltaDe(v, rel)
            If abs = "" Then Return Array.Empty(Of Byte)()
            Return File.ReadAllBytes(abs)
        End If
    End Function

    Private Sub DespublicarTodo()
        For Each clave In _publicadas.Keys.ToList()
            Try
                FilesDictionary_class.RemoveDictionaryEntry(clave)
            Catch
                ' Quitar una clave que ya no está no es un problema.
            End Try
        Next
        _publicadas.Clear()
    End Sub

    ''' <summary>Desmonta todo: saca del diccionario de la librería lo que publiqué y tira las cachés.
    ''' Se llama al cambiar de juego o al tocar la lista de fuentes — las dos cachés por path relativo
    ''' (la de bytes de la librería y la de texturas GL del modelo) quedarían mintiendo si no.</summary>
    Public Sub Limpiar()
        DespublicarTodo()
        FilesDictionary_class.ClearBytesCache()
        _vivas.Clear()
        _juegoMontado = Nothing
        ReiniciarDiagnostico()
    End Sub
End Class
