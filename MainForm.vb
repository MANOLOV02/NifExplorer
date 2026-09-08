Option Strict On

Imports System.Drawing
Imports System.IO
Imports System.Windows.Forms

''' <summary>
''' Fast and High Quality Nif Explorer.
'''
''' <para>Navega los NIF de las fuentes que el usuario pone (carpetas sueltas y BA2/BSA, EN ORDEN),
''' los muestra en el preview de la librería y los exporta con sus texturas respetando los paths
''' relativos del juego.</para>
'''
''' <para><b>Las reglas del visor</b>, todas medidas antes de escribirse:</para>
''' <list type="bullet">
''' <item>Un NIF a la vez. El juego lo declara el header del NIF y eso elige el shader, el rig de luces
''' y CUÁL de las dos listas de fuentes está activa (FO4 y SSE comparten nombres de path).</item>
''' <item>Sin esqueleto: no se carga ninguno y no hace falta. Sin esqueleto cada hueso cae al nodo del
''' NIF y la paleta resuelve el bind correcto sola — MEDIDO contra el rango en Z de la geometría, ver
''' el comentario del paso 6 de <c>CargarDesdeBytes</c>. La librería no necesita ayuda acá.</item>
''' <item>Las helper shapes (colisiones, proxies) se dibujan: es un explorador, no un juego.</item>
''' <item>La resolución de assets es la lista del usuario, primera fuente que tenga el path gana. Sin
''' orden de carga, sin Plugins.txt, sin autodetección.</item>
''' </list>
''' </summary>
Partial Class MainForm

    Private ReadOnly _cfg As ConfigFuentes = ConfigFuentes.Cargar()
    Private ReadOnly _resolutor As New Resolutor()
    Private _preview As PreviewControl
    Private _juego As Config_App.Game_Enum = Config_App.Game_Enum.Fallout4
    Private _cargando As Boolean = False

    ' Estado del NIF en pantalla (lo que necesita Export).
    Private _nif As Nifcontent_Class_Manolo
    Private _shapes As List(Of IRenderableShape)
    Private _relNif As String = ""
    Private _bytesNif As Byte()
    Private ReadOnly _dependencias As New List(Of String)

    Private Const Dummy As String = "…"

    ''' <summary>NIF a abrir apenas la ventana esté mostrada, y carpeta a la que exportarlo enseguida
    ''' (argumentos de línea de comandos: <c>NifExplorer.exe archivo.nif [--export carpeta]</c>).</summary>
    Private ReadOnly _nifInicial As String
    Private ReadOnly _exportarA As String

    Public Sub New()
        Me.New(Nothing, Nothing)
    End Sub

    Public Sub New(nifInicial As String, Optional exportarA As String = Nothing)
        InitializeComponent()
        ' Nombre del Designer + version REAL del ensamblado. Aca y no en el .Designer.vb, que el disenador
        ' reescribe. Nif Explorer se lleva TRES DLL propios: saber cual es esta importa mas todavia.
        Me.Text = VersionGate.TituloConVersion(Me.Text)
        _nifInicial = nifInicial
        _exportarA = exportarA
    End Sub

    ' ══════════════════════════════════ arranque ══════════════════════════════════

    Private Sub MainForm_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        ' El layout ya está hecho: recién ahora el SplitContainer tiene ancho real.
        Try
            split.Panel1MinSize = 260
            split.Panel2MinSize = 420
            splitIzq.Panel1MinSize = 60
            splitIzq.Panel2MinSize = 120
        Catch
        End Try

        ' Config de la LIBRERÍA (luces, sombras, ajustes de render). Vive en el config.json de ESTE exe,
        ' así que no comparte estado con Wardrobe Manager ni con NPC Manager.
        Try
            Config_App.LoadConfig()
        Catch
        End Try
        ' Decisión del usuario: las helper shapes se ven (colisiones, proxies de física, sangre de armas).
        Config_App.Current.Setting_ShowHelperShapes = True
        ' Nada del visor depende de esta raíz: todas las entradas que publico son ABSOLUTAS. Se le pone
        ' una ruta válida igual, para no dejar la propiedad vacía por si algún camino de la lib la usa.
        FilesDictionary_class.FO4Path = Application.StartupPath

        AddHandler lstFuentes.DrawItem, AddressOf lstFuentes_DrawItem

        tips.SetToolTip(btnAddArchive, "Add BA2/BSA archives as sources")
        tips.SetToolTip(btnAddCarpeta, "Add a loose folder as a source")
        tips.SetToolTip(btnSubir, "Move up — the topmost source wins. Loose folders always resolve before archives.")
        tips.SetToolTip(btnBajar, "Move down")
        tips.SetToolTip(lstFuentes, "Resolution order, top wins. Loose folders (blue) always come before BA2/BSA archives, like the engine.")
        tips.SetToolTip(btnQuitar, "Remove source")
        tips.SetToolTip(btnLimpiar, "Remove ALL sources of the active game")
        tips.SetToolTip(btnExportar, "Export the open NIF + its textures/materials, keeping relative paths")

        CambiarJuego(_juego)
    End Sub

    ''' <summary>⛔ EL PREVIEW SE CREA EN Shown, NO EN Load, igual que en Wardrobe Manager
    ''' (<c>OSPManager_Form_Shown</c>) y en NPC Manager: el <c>GLControl</c> necesita un handle ya
    ''' realizado para crear su contexto de OpenGL. Creado en Load, el contexto nace contra una ventana
    ''' que todavía no existe.</summary>
    Private Sub MainForm_Shown(sender As Object, e As EventArgs) Handles MyBase.Shown
        If _preview IsNot Nothing Then Return

        ' ⛔ LAS PROPORCIONES SE FIJAN ACÁ, NO EN Load: recién con la ventana ya MOSTRADA y maximizada los
        ' dos SplitContainer tienen su tamaño real. Puestas en Load se calculaban contra los 150 px del
        ' control sin realizar y salía cualquier cosa. Quedan PROPORCIONALES (FixedPanel.None, el default)
        ' a propósito: así el tercio y las dos mitades se sostienen si después redimensionás la ventana.
        Try
            split.SplitterDistance = Math.Max(split.Panel1MinSize, CInt(split.Width / 3))
        Catch
        End Try
        Try
            splitIzq.SplitterDistance = Math.Max(splitIzq.Panel1MinSize, CInt(splitIzq.Height / 2))
        Catch
        End Try

        _preview = New PreviewControl With {.Dock = DockStyle.Fill}
        panelPreview.Controls.Add(_preview)
        _preview.BringToFront()
        _preview.ApplyResize(True)

        ' El piso/grilla sale de la misma config que el resto del stack, igual que hace WM al arrancar.
        Try
            _preview.Model.Floor.Enabled = Config_App.Current.Settings_RenderGrid.Enabled
            _preview.Model.Floor.Color = Config_App.Current.RenderGridColor
            _preview.Model.Floor.Size = Config_App.Current.Settings_RenderGrid.Size
            _preview.Model.Floor.StepSize = Config_App.Current.Settings_RenderGrid.StepSize
            _preview.Model.Floor.Rebuild()
        Catch
        End Try

        Try
            _preview.ApplyRenderSettingsFromConfig()
        Catch
        End Try

        _preview.ClearRender("Pick a NIF from the tree")

        If Not String.IsNullOrEmpty(_nifInicial) Then
            Try
                Using New EsperaVisual(Me)
                    CargarDesdeBytes(IO.File.ReadAllBytes(_nifInicial), IO.Path.GetFileName(_nifInicial))
                End Using
            Catch ex As Exception
                MostrarEstado($"Could not open {IO.Path.GetFileName(_nifInicial)}: {ex.Message}", Tema.ColRojo)
                Return
            End Try

            If Not String.IsNullOrEmpty(_exportarA) Then Exportar(_exportarA)
        End If
    End Sub

    Private Sub MainForm_FormClosing(sender As Object, e As FormClosingEventArgs) Handles MyBase.FormClosing
        Try
            _cfg.Guardar()
        Catch
        End Try
        Try
            Config_App.SaveConfig()
        Catch
        End Try
        Try
            _preview?.BeginTeardown()
        Catch
        End Try
    End Sub

    ' ══════════════════════════════════ juego ══════════════════════════════════

    Private Sub CambiarJuego(j As Config_App.Game_Enum)
        _juego = j
        Config_App.Current.Game = j
        Tema.MarcarActivo(btnJuegoFO4, j = Config_App.Game_Enum.Fallout4)
        Tema.MarcarActivo(btnJuegoSSE, j = Config_App.Game_Enum.Skyrim)
        RefrescarLista()
        Montar()
    End Sub

    Private Sub btnJuegoFO4_Click(sender As Object, e As EventArgs) Handles btnJuegoFO4.Click
        If _juego <> Config_App.Game_Enum.Fallout4 Then CambiarJuego(Config_App.Game_Enum.Fallout4)
    End Sub

    Private Sub btnJuegoSSE_Click(sender As Object, e As EventArgs) Handles btnJuegoSSE.Click
        If _juego <> Config_App.Game_Enum.Skyrim Then CambiarJuego(Config_App.Game_Enum.Skyrim)
    End Sub

    ' ══════════════════════════════════ fuentes ══════════════════════════════════

    Private Sub RefrescarLista()
        lstFuentes.BeginUpdate()
        lstFuentes.Items.Clear()
        For Each r In _cfg.Lista(_juego)
            lstFuentes.Items.Add(New Fuente With {.Ruta = r})
        Next
        lstFuentes.EndUpdate()
    End Sub

    Private Sub lstFuentes_DrawItem(sender As Object, e As DrawItemEventArgs)
        If e.Index < 0 Then Return
        Dim f = CType(lstFuentes.Items(e.Index), Fuente)
        Dim sel = (e.State And DrawItemState.Selected) = DrawItemState.Selected
        Using fondo As New SolidBrush(If(sel, Tema.ColSuperficieAlta, Tema.ColSuperficie))
            e.Graphics.FillRectangle(fondo, e.Bounds)
        End Using
        Dim color = If(Not f.Existe, Tema.ColRojo, If(f.EsArchive, Tema.ColFrente, Tema.ColAcento))
        Using pincel As New SolidBrush(color)
            Dim texto = $"{e.Index + 1}.  {f.Nombre}" & If(f.Existe, "", "   (missing)")
            e.Graphics.DrawString(texto, Tema.Letra, pincel, e.Bounds.X + 4, e.Bounds.Y + 2)
        End Using
    End Sub

    Private Sub btnAddArchive_Click(sender As Object, e As EventArgs) Handles btnAddArchive.Click
        Using d As New OpenFileDialog With {
            .Title = "Add BA2/BSA sources",
            .Filter = "Bethesda archives (*.ba2;*.bsa)|*.ba2;*.bsa|All files (*.*)|*.*",
            .Multiselect = True}
            If d.ShowDialog(Me) <> DialogResult.OK Then Return
            AgregarFuentes(d.FileNames)
        End Using
    End Sub

    Private Sub btnAddCarpeta_Click(sender As Object, e As EventArgs) Handles btnAddCarpeta.Click
        Using d As New FolderBrowserDialog With {.Description = "Add a loose folder as a source"}
            If d.ShowDialog(Me) <> DialogResult.OK Then Return
            AgregarFuentes({d.SelectedPath})
        End Using
    End Sub

    ''' <summary>Agrega rutas a la lista del juego activo, sin duplicar. Una carpeta que contenga
    ''' archives NO los agrega sola: el usuario decide qué monta y en qué orden, que es todo el punto.</summary>
    Private Sub AgregarFuentes(rutas As IEnumerable(Of String))
        Dim lista = _cfg.Lista(_juego)
        Dim agregadas = 0
        Dim sueltasArriba As New List(Of String)

        For Each r In rutas
            If String.IsNullOrWhiteSpace(r) Then Continue For
            If lista.Any(Function(x) String.Equals(x, r, StringComparison.OrdinalIgnoreCase)) Then Continue For
            lista.Add(r)
            agregadas += 1

            For Each carpeta In CarpetasDelData(r)
                If lista.Any(Function(x) String.Equals(x, carpeta, StringComparison.OrdinalIgnoreCase)) Then Continue For
                If sueltasArriba.Any(Function(x) String.Equals(x, carpeta, StringComparison.OrdinalIgnoreCase)) Then Continue For
                sueltasArriba.Add(carpeta)
            Next
        Next

        If sueltasArriba.Count > 0 Then
            lista.InsertRange(0, sueltasArriba)
            agregadas += sueltasArriba.Count
        End If

        If agregadas = 0 Then Return
        _cfg.Guardar()
        RefrescarLista()
        Montar()
    End Sub

    ''' <summary>Las carpetas de assets del Data al que pertenece <paramref name="ruta"/>, para agregarlas
    ''' junto con ella.
    ''' <para><b>Camina hacia ATRÁS</b> hasta encontrar una carpeta que tenga alguna de
    ''' <c>Materials</c> / <c>Meshes</c> / <c>Textures</c> adentro: ese es el Data. Sirve igual si lo que
    ''' agregaste fue un <c>.ba2</c> (que vive EN el Data), la carpeta <c>Data</c> entera, o
    ''' <c>Data\Meshes\Armor\Loquesea</c> — que es el gesto normal cuando querés mirar un mod concreto.
    ''' Sin esto ese mod se ve sin texturas, porque un path <c>Textures\…</c> no existe bajo
    ''' <c>Meshes\Armor</c>.</para>
    ''' <para>Se saltean las que son la carpeta elegida o antepasadas de ella: si elegiste
    ''' <c>Data\Meshes\Armor</c> no tiene sentido agregar además <c>Data\Meshes</c>, y si elegiste el
    ''' <c>Data</c> pelado no hace falta nada (los tres paths ya resuelven bajo él).</para>
    ''' <para>El límite de 10 niveles es una red contra rutas raras; un Data real está a uno o dos.</para></summary>
    Private Shared Function CarpetasDelData(ruta As String) As List(Of String)
        Dim salida As New List(Of String)
        Try
            Dim completa = Path.GetFullPath(ruta)
            Dim f As New Fuente With {.Ruta = ruta}
            Dim elegida = If(f.EsArchive, Path.GetDirectoryName(completa), completa)
            If String.IsNullOrEmpty(elegida) Then Return salida

            Dim nombres = {"Materials", "Meshes", "Textures"}
            Dim data As String = Nothing
            Dim actual = elegida
            For nivel = 0 To 9
                If String.IsNullOrEmpty(actual) Then Exit For
                If nombres.Any(Function(n) Directory.Exists(Path.Combine(actual, n))) Then
                    data = actual
                    Exit For
                End If
                Dim padre = Path.GetDirectoryName(actual)
                If String.IsNullOrEmpty(padre) OrElse String.Equals(padre, actual, StringComparison.OrdinalIgnoreCase) Then Exit For
                actual = padre
            Next
            If data Is Nothing Then Return salida

            For Each n In nombres
                Dim carpeta = Path.Combine(data, n)
                If Not Directory.Exists(carpeta) Then Continue For
                ' ¿es la elegida, o la contiene? Entonces ya está cubierta.
                Dim conBarra = carpeta.TrimEnd(Path.DirectorySeparatorChar) & Path.DirectorySeparatorChar
                If String.Equals(carpeta.TrimEnd(Path.DirectorySeparatorChar), elegida.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) Then Continue For
                If elegida.StartsWith(conBarra, StringComparison.OrdinalIgnoreCase) Then Continue For
                salida.Add(carpeta)
            Next
        Catch
            ' Ruta inválida o sin permisos: no se agrega nada extra, la fuente elegida entra igual.
        End Try
        Return salida
    End Function

    Private Sub btnQuitar_Click(sender As Object, e As EventArgs) Handles btnQuitar.Click
        Dim i = lstFuentes.SelectedIndex
        If i < 0 Then Return
        _cfg.Lista(_juego).RemoveAt(i)
        _cfg.Guardar()
        RefrescarLista()
        If lstFuentes.Items.Count > 0 Then lstFuentes.SelectedIndex = Math.Min(i, lstFuentes.Items.Count - 1)
        Montar()
    End Sub

    ''' <summary>Vacía la lista ENTERA del juego activo. Toca sólo la lista del juego que se está
    ''' viendo —nunca las dos—, por la misma razón por la que hay dos listas separadas: los paths de
    ''' FO4 y de SSE se pisan y mezclarlos sangraría texturas de un juego en el NIF del otro.
    ''' <para>Pide confirmación porque el gesto es irreversible y se PERSISTE al toque: se pierde el
    ''' orden que el usuario armó a mano con ▲▼, no sólo las rutas.</para></summary>
    Private Sub btnLimpiar_Click(sender As Object, e As EventArgs) Handles btnLimpiar.Click
        Dim lista = _cfg.Lista(_juego)
        If lista.Count = 0 Then Return

        Dim juego = If(_juego = Config_App.Game_Enum.Fallout4, "Fallout 4", "Skyrim SE")
        If MessageBox.Show(Me,
                           $"Remove all {lista.Count} {juego} sources?",
                           "Clear sources",
                           MessageBoxButtons.YesNo,
                           MessageBoxIcon.Warning,
                           MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return

        lista.Clear()
        _cfg.Guardar()
        RefrescarLista()
        Montar()
    End Sub

    Private Sub btnSubir_Click(sender As Object, e As EventArgs) Handles btnSubir.Click
        Mover(-1)
    End Sub

    Private Sub btnBajar_Click(sender As Object, e As EventArgs) Handles btnBajar.Click
        Mover(1)
    End Sub

    Private Sub Mover(delta As Integer)
        Dim i = lstFuentes.SelectedIndex
        If i < 0 Then Return
        Dim j = i + delta
        Dim lista = _cfg.Lista(_juego)
        If j < 0 OrElse j >= lista.Count Then Return
        Dim tmp = lista(i)
        lista(i) = lista(j)
        lista(j) = tmp
        _cfg.Guardar()
        RefrescarLista()
        lstFuentes.SelectedIndex = j
        Montar()
    End Sub

    ''' <summary>Monta la lista activa y reconstruye el árbol. Tocar la lista invalida TODO lo publicado
    ''' y las dos cachés por path relativo (la de bytes de la librería y la de texturas GL del modelo),
    ''' así que el preview se vacía: la próxima selección recarga limpio.</summary>
    Private Sub Montar()
        _tieneNif.Clear()
        Using New EsperaVisual(Me)
            _resolutor.Montar(_juego, _cfg.Lista(_juego), Sub(s) MostrarEstado(s, Tema.ColFrenteTenue))
        End Using
        LimpiarPreview()
        ConstruirArbol()

        ' ⛔ Where(...).Count(), no Count(predicado): sobre un IReadOnlyList VB resuelve `Count` como la
        ' PROPIEDAD y después intenta indexarla con la lambda (BC32016).
        Dim archivos = _resolutor.Fuentes.Where(Function(v) v.Fuente.EsArchive).Count()
        Dim nifs = _resolutor.Fuentes.Where(Function(v) v.Nifs IsNot Nothing).Sum(Function(v) v.Nifs.Count)
        Dim rotas = _resolutor.Fuentes.Where(Function(v) Not v.Existe OrElse v.Problema <> "").Count()
        Dim msg = $"{_resolutor.Fuentes.Count} sources ({archivos} archives) · {nifs:N0} NIF indexed"
        If rotas > 0 Then msg &= $" · {rotas} unavailable"
        MostrarEstado(msg, If(rotas > 0, Tema.ColAlerta, Tema.ColFrenteTenue))
    End Sub

    ' ══════════════════════════════════ árbol ══════════════════════════════════

    ''' <summary>Memo de "¿esta carpeta tiene algún .nif adentro?". <c>Any()</c> sobre la enumeración
    ''' PEREZOSA corta en el primer acierto, así que una carpeta CON nifs cuesta nada; la que cuesta es
    ''' la que no tiene ninguno —justamente <c>Textures</c>, que es la que hay que esconder— y por eso el
    ''' resultado se memoiza y se paga una sola vez por carpeta por sesión. Se limpia al remontar.</summary>
    Private ReadOnly _tieneNif As New Dictionary(Of String, Boolean)(StringComparer.OrdinalIgnoreCase)

    Private Function TieneNifs(dir As String) As Boolean
        Dim r As Boolean
        If _tieneNif.TryGetValue(dir, r) Then Return r
        r = False
        Try
            r = Directory.EnumerateFiles(dir, "*.nif", SearchOption.AllDirectories).Any()
        Catch
            ' Sin permisos: se la trata como vacía en vez de tirar.
        End Try
        _tieneNif(dir) = r
        Return r
    End Function

    Private Class NodoTag
        Public Property Fuente As Resolutor.FuenteViva
        Public Property Rel As String = ""
        Public Property EsCarpeta As Boolean
    End Class

    Private Sub ConstruirArbol()
        treeNifs.BeginUpdate()
        treeNifs.Nodes.Clear()

        Dim filtro = txtFiltro.Text.Trim()
        If filtro.Length >= 2 Then
            ConstruirFiltrado(filtro)
        Else
            For Each v In _resolutor.Fuentes
                Dim n = treeNifs.Nodes.Add(v.Fuente.Nombre)
                n.Tag = New NodoTag With {.Fuente = v, .Rel = "", .EsCarpeta = True}
                If Not v.Existe Then
                    n.Text &= "   (missing)"
                    n.ForeColor = Tema.ColRojo
                    Continue For
                End If
                If v.Problema <> "" Then
                    n.Text &= "   (" & v.Problema & ")"
                    n.ForeColor = Tema.ColAlerta
                    Continue For
                End If

                ' Una fuente SIN un solo .nif (la carpeta Textures que se agrega sola, un archive de
                ' puras texturas) se muestra apagada y sin desplegable: sigue estando —resuelve assets y
                ' se ve en la lista de arriba— pero no ensucia el árbol con un nodo que no lleva a nada.
                Dim conNifs = If(v.Fuente.EsArchive,
                                 v.Nifs IsNot Nothing AndAlso v.Nifs.Count > 0,
                                 TieneNifs(v.Fuente.Ruta))
                If Not conNifs Then
                    n.Text &= "   (assets only)"
                    n.ForeColor = Tema.ColFrenteTenue
                    Continue For
                End If

                n.ForeColor = If(v.Fuente.EsArchive, Tema.ColFrente, Tema.ColAcento)
                n.Nodes.Add(Dummy)
            Next
        End If

        treeNifs.EndUpdate()
    End Sub

    ''' <summary>Modo filtro: lista plana con los .nif que matchean, de todas las fuentes, con tope.
    ''' Para las carpetas sueltas la enumeración es PEREZOSA y se corta al llegar al tope, así que
    ''' filtrar sobre un Data enorme no cuelga la UI.</summary>
    Private Sub ConstruirFiltrado(filtro As String)
        Const Tope As Integer = 400
        Dim n = 0
        For Each v In _resolutor.Fuentes
            If Not v.Existe Then Continue For
            If v.Fuente.EsArchive Then
                If v.Nifs Is Nothing Then Continue For
                For Each p In v.Nifs
                    If p.IndexOf(filtro, StringComparison.OrdinalIgnoreCase) < 0 Then Continue For
                    AgregarHoja(v, p)
                    n += 1
                    If n >= Tope Then Exit For
                Next
            Else
                Try
                    For Each abs In Directory.EnumerateFiles(v.Fuente.Ruta, "*.nif", SearchOption.AllDirectories)
                        Dim rel = abs.Substring(v.Fuente.Ruta.TrimEnd("\"c).Length).TrimStart("\"c)
                        If rel.IndexOf(filtro, StringComparison.OrdinalIgnoreCase) < 0 Then Continue For
                        AgregarHoja(v, rel)
                        n += 1
                        If n >= Tope Then Exit For
                    Next
                Catch
                    ' Carpeta inaccesible a mitad del recorrido: lo que se alcanzó a listar vale.
                End Try
            End If
            If n >= Tope Then Exit For
        Next
        If n = 0 Then treeNifs.Nodes.Add("(no matches)").ForeColor = Tema.ColFrenteTenue
        If n >= Tope Then treeNifs.Nodes.Add($"(showing first {Tope})").ForeColor = Tema.ColAlerta
    End Sub

    Private Sub AgregarHoja(v As Resolutor.FuenteViva, rel As String)
        Dim n = treeNifs.Nodes.Add(rel)
        n.Tag = New NodoTag With {.Fuente = v, .Rel = rel, .EsCarpeta = False}
        n.ToolTipText = v.Fuente.Nombre
    End Sub

    Private Sub treeNifs_BeforeExpand(sender As Object, e As TreeViewCancelEventArgs) Handles treeNifs.BeforeExpand
        Dim n = e.Node
        If n.Nodes.Count <> 1 OrElse n.Nodes(0).Tag IsNot Nothing OrElse n.Nodes(0).Text <> Dummy Then Return
        n.Nodes.Clear()
        Dim t = TryCast(n.Tag, NodoTag)
        If t Is Nothing Then Return
        Using New EsperaVisual(Me)
            Poblar(n, t)
        End Using
    End Sub

    Private Sub Poblar(nodo As TreeNode, t As NodoTag)
        Dim carpetas As New SortedSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Dim archivos As New List(Of String)

        If t.Fuente.Fuente.EsArchive Then
            Dim pref = If(t.Rel = "", "", t.Rel & "\")
            If t.Fuente.Nifs IsNot Nothing Then
                For Each p In t.Fuente.Nifs
                    If pref <> "" AndAlso Not p.StartsWith(pref, StringComparison.OrdinalIgnoreCase) Then Continue For
                    Dim resto = p.Substring(pref.Length)
                    Dim i = resto.IndexOf("\"c)
                    If i > 0 Then
                        carpetas.Add(resto.Substring(0, i))
                    ElseIf i < 0 AndAlso resto <> "" Then
                        archivos.Add(resto)
                    End If
                Next
            End If
        Else
            ' UN SOLO NIVEL. Recorrer recursivamente para saber qué subcarpeta tiene NIF sería pagar el
            ' scan completo de un Data entero por cada expand.
            Dim baseDir = If(t.Rel = "", t.Fuente.Fuente.Ruta, Path.Combine(t.Fuente.Fuente.Ruta, t.Rel))
            Try
                For Each d In Directory.EnumerateDirectories(baseDir)
                    ' Sólo las que llevan a algún .nif: sin esto, abrir un Data despliega el árbol entero
                    ' de Textures, Sound y Interface, que es ruido puro en un explorador de mallas.
                    If TieneNifs(d) Then carpetas.Add(Path.GetFileName(d))
                Next
                For Each f In Directory.EnumerateFiles(baseDir, "*.nif")
                    archivos.Add(Path.GetFileName(f))
                Next
            Catch
            End Try
            archivos.Sort(StringComparer.OrdinalIgnoreCase)
        End If

        For Each c In carpetas
            Dim hijo = nodo.Nodes.Add(c)
            hijo.Tag = New NodoTag With {.Fuente = t.Fuente, .Rel = If(t.Rel = "", c, t.Rel & "\" & c), .EsCarpeta = True}
            hijo.ForeColor = Tema.ColFrenteTenue
            hijo.Nodes.Add(Dummy)
        Next
        For Each a In archivos
            Dim hijo = nodo.Nodes.Add(a)
            hijo.Tag = New NodoTag With {.Fuente = t.Fuente, .Rel = If(t.Rel = "", a, t.Rel & "\" & a), .EsCarpeta = False}
        Next
    End Sub

    Private Sub txtFiltro_TextChanged(sender As Object, e As EventArgs) Handles txtFiltro.TextChanged
        ConstruirArbol()
    End Sub

    Private Sub treeNifs_AfterSelect(sender As Object, e As TreeViewEventArgs) Handles treeNifs.AfterSelect
        Dim t = TryCast(e.Node?.Tag, NodoTag)
        If t Is Nothing OrElse t.EsCarpeta Then Return
        AbrirNif(t.Fuente, t.Rel)
    End Sub

    ' ══════════════════════════════════ carga del NIF ══════════════════════════════════

    Private Sub AbrirNif(v As Resolutor.FuenteViva, rel As String)
        If _cargando Then Return
        _cargando = True
        Try
            Using New EsperaVisual(Me)
                ' El archive pudo cambiar en disco desde que lo indexé (WM Pack, el packer de FaceGen).
                ' Los índices viejos sobre el archive nuevo devuelven los bytes de OTRA entrada, sin error.
                If _resolutor.RevalidarArchives() Then ConstruirArbol()

                Dim bytes = _resolutor.BytesDe(v, rel)
                If bytes Is Nothing OrElse bytes.Length = 0 Then
                    LimpiarPreview("Could not read " & rel)
                    MostrarEstado("Could not read " & rel, Tema.ColRojo)
                    Return
                End If
                CargarDesdeBytes(bytes, rel)
            End Using
        Finally
            _cargando = False
        End Try
    End Sub

    Private Sub CargarDesdeBytes(bytes As Byte(), rel As String)
        _bytesNif = bytes
        _relNif = rel
        _dependencias.Clear()

        ' ── 1. leer el NIF ──
        Dim nif As New Nifcontent_Class_Manolo()
        Try
            nif.Load_Manolo(bytes)
        Catch ex As Exception
            LimpiarPreview("Unreadable NIF")
            MostrarEstado($"Unreadable NIF: {ex.Message}", Tema.ColRojo)
            Return
        End Try

        ' ── 2. el juego lo declara el header ──
        Dim ver = nif.Header.Version
        Dim juegoNif As Config_App.Game_Enum
        Dim aviso As String = ""
        If ver.IsFO4() Then
            juegoNif = Config_App.Game_Enum.Fallout4
        ElseIf ver.IsSSE() Then
            juegoNif = Config_App.Game_Enum.Skyrim
        ElseIf ver.IsSK() Then
            ' Oldrim (stream 83). Se dibuja con las leyes de SSE, que es lo más cercano que hay, pero
            ' la divergencia está medida (rim espurio, sin tint) y el usuario tiene que saberlo.
            juegoNif = Config_App.Game_Enum.Skyrim
            aviso = " · Oldrim (stream 83): drawn with SSE rules"
        Else
            LimpiarPreview("Unsupported NIF version")
            MostrarEstado($"Unsupported NIF version (stream {ver.StreamVersion}, user {ver.UserVersion}) — this explorer covers Fallout 4 and Skyrim.", Tema.ColRojo)
            Return
        End If

        If juegoNif <> _juego Then CambiarJuego(juegoNif)

        ' ── 3. shapes ──
        Try
            _shapes = NifRenderableShape.FromNif(nif).Cast(Of IRenderableShape)().ToList()
        Catch ex As Exception
            LimpiarPreview("NIF could not be prepared")
            MostrarEstado($"NIF could not be prepared: {ex.Message}", Tema.ColRojo)
            Return
        End Try
        _nif = nif

        If _shapes Is Nothing OrElse _shapes.Count = 0 Then
            LimpiarPreview("No renderable shapes")
            MostrarEstado($"{Etiqueta(juegoNif)} · {Path.GetFileName(rel)} · no renderable shapes" & aviso, Tema.ColAlerta)
            Return
        End If

        _resolutor.ReiniciarDiagnostico()

        ' ── 4. materiales ──
        ' Se publican DESPUÉS de FromNif porque el path del material sale del shader, y se re-resuelven
        ' con el cargador de la propia librería: la clave y la deserialización son las suyas, no una copia.
        For Each sh In _shapes
            Dim rm = sh.ShapeMaterial
            If rm Is Nothing OrElse String.IsNullOrEmpty(rm.path) Then Continue For
            Dim clave = FO4UnifiedMaterial_Class.CorrectMaterialPath(rm.path)
            If Not _resolutor.Publicar(clave) Then Continue For
            _dependencias.Add(clave)
            Try
                Dim m = MaterialResolver.TryLoadMaterialFromDictionary(rm.path, rm.material, sh.NifShape, nif)
                If m IsNot Nothing Then rm.material = m
            Catch
                ' Material ilegible: queda el reconstruido desde el shader, que es lo que la lib ya hizo.
            End Try
        Next

        ' ── 5. texturas ──
        ' La lista de 14 slots sale de la MISMA propiedad que consulta el render (MaterialData
        ' .Textures_Path_List, ya normalizada con CorrectTexturePath). Nada de reimplementar el mapeo.
        For Each sh In _shapes
            For Each p In PathsDeTextura(sh)
                If String.IsNullOrEmpty(p) Then Continue For
                If _resolutor.Publicar(p) Then _dependencias.Add(p)
            Next
        Next

        ' ── 6. render ──
        ' ⛔⛔ ACÁ NO VA NADA DE BIND POSE: vaciar la paleta de huesos para que el skinning sea un no-op
        ' ("los vértices ya están en bind") es EL DEFECTO, no el arreglo. Sin esqueleto, cada hueso cae al
        ' fallback del nodo del NIF en SkinningHelper.ExtractSkinnedGeometry (rama sin match en
        ' SkeletonDictionary: bindT = Transform_Class.GetGlobalTransform) y la paleta resuelve el bind
        ' CORRECTO sola. MEDIDO sobre los NIF vanilla de los dos juegos, rango en Z de la geometría:
        '     FO4 BaseFemaleBody    crudo [-120,94, -6,73]   paleta [-0,09, 114,11]
        '     FO4 VaultNumber       crudo [ -25,94, -9,47]   paleta [94,91, 111,38]  (el número, al pecho)
        '     SSE FemaleUnderwear   crudo [-108,96, -6,36]   paleta [11,38, 113,99]
        ' Los vértices crudos dejan el cuerpo ENTERO bajo el piso. La librería ya hace lo correcto: no hay
        ' que ayudarla.
        Try
            _preview.RenderShapes(_shapes)
        Catch ex As Exception
            MostrarEstado($"Render failed: {ex.Message}", Tema.ColRojo)
            Return
        End Try

        ' ── 7. diagnóstico honesto ──
        Dim pedidas = _resolutor.Resueltas.Select(Function(x) x.Clave).
                                 Distinct(StringComparer.OrdinalIgnoreCase).Count() + _resolutor.NoResueltas.Count
        Dim faltan = _resolutor.NoResueltas.Count
        Dim texto = $"{Etiqueta(juegoNif)} · {Path.GetFileName(rel)} · {_shapes.Count} shapes · {pedidas - faltan}/{pedidas} assets{aviso}"
        MostrarEstado(texto, If(faltan > 0, Tema.ColAlerta, Tema.ColFrenteTenue))

        If faltan > 0 Then
            tips.SetToolTip(lblEstado, "Not found in any source:" & vbCrLf &
                            String.Join(vbCrLf, _resolutor.NoResueltas.Take(20)) &
                            If(faltan > 20, vbCrLf & $"… and {faltan - 20} more", ""))
        Else
            tips.SetToolTip(lblEstado, "")
        End If
    End Sub

    ''' <summary>Los paths de textura de una shape, por la MISMA propiedad que usa el render.
    ''' Se arma una <c>MaterialData</c> transitoria con el material de la shape como override — el mismo
    ''' patrón que la librería usa para los overlays de LooksMenu.</summary>
    Private Shared Function PathsDeTextura(sh As IRenderableShape) As IEnumerable(Of String)
        If sh.ShapeMaterial Is Nothing Then Return Array.Empty(Of String)()
        Dim md As New PreviewModel.RenderableMesh.MeshData_Class()
        Dim mat As New PreviewModel.RenderableMesh.MaterialData(md) With {.OverrideRelatedMaterial = sh.ShapeMaterial}
        Return mat.Textures_Path_List.ToList()
    End Function

    Private Shared Function Etiqueta(j As Config_App.Game_Enum) As String
        Return If(j = Config_App.Game_Enum.Fallout4, "Fallout 4", "Skyrim SE")
    End Function

    Private Sub LimpiarPreview(Optional texto As String = "Empty")
        _nif = Nothing
        _shapes = Nothing
        _relNif = ""
        _bytesNif = Nothing
        _dependencias.Clear()
        Try
            _preview?.ClearRender(texto)
        Catch
        End Try
    End Sub

    Private Sub MostrarEstado(texto As String, color As Color)
        lblEstado.Text = texto
        lblEstado.ForeColor = color
        lblJuego.Text = Etiqueta(_juego)
        lblEstado.Refresh()
    End Sub

    ' ══════════════════════════════════ botones ══════════════════════════════════

    Private Sub btnLuces_Click(sender As Object, e As EventArgs) Handles btnLuces.Click
        AbrirDialogoRig()
    End Sub

    ''' <summary>El diálogo de la librería, el mismo que usan Wardrobe Manager y NPC Manager: trae las dos
    ''' pestañas (luces/sombras y render), así que las luces y los ajustes son exactamente los del resto
    ''' del stack. Un solo botón porque es un solo diálogo.</summary>
    Private Sub AbrirDialogoRig()
        Using dlg As New LightRigForm With {.AllowHiddenSegments = True}
            AddHandler dlg.LightsChanged, AddressOf OnLucesCambiadas
            AddHandler dlg.RenderSettingsChanged, AddressOf OnRenderCambiado
            Try
                dlg.ShowDialog(Me)
            Finally
                RemoveHandler dlg.LightsChanged, AddressOf OnLucesCambiadas
                RemoveHandler dlg.RenderSettingsChanged, AddressOf OnRenderCambiado
            End Try
        End Using
    End Sub

    Private Sub OnLucesCambiadas()
        If _preview Is Nothing OrElse _preview.IsDisposed Then Return
        _preview.UpdateRequired = True
        _preview.Update()
    End Sub

    ''' <summary>La pestaña Render toca cosas que NO se arreglan repintando (recálculo de normales,
    ''' welding, skinning): hay que empujarlas al modelo y volver a armar la geometría.</summary>
    Private Sub OnRenderCambiado()
        If _preview Is Nothing OrElse _preview.IsDisposed Then Return
        _preview.ApplyRenderSettingsFromConfig()
        If _shapes IsNot Nothing AndAlso _shapes.Count > 0 Then
            Try
                _preview.RenderShapes(_shapes)
            Catch
            End Try
        End If
    End Sub

    Private Sub btnExportar_Click(sender As Object, e As EventArgs) Handles btnExportar.Click
        If _bytesNif Is Nothing OrElse _bytesNif.Length = 0 Then
            MostrarEstado("Nothing to export — open a NIF first.", Tema.ColAlerta)
            Return
        End If

        Using d As New FolderBrowserDialog With {
            .Description = "Export the NIF and its textures here (relative paths are kept)",
            .SelectedPath = If(Directory.Exists(_cfg.UltimoDirectorioExport), _cfg.UltimoDirectorioExport, "")}
            If d.ShowDialog(Me) <> DialogResult.OK Then Return
            _cfg.UltimoDirectorioExport = d.SelectedPath
            _cfg.Guardar()
            Exportar(d.SelectedPath)
        End Using
    End Sub

    ''' <summary>Escribe el NIF abierto y sus dependencias bajo <paramref name="destino"/>, conservando
    ''' los paths relativos del juego. Lo comparten el botón y el flag <c>--export</c>.</summary>
    Private Sub Exportar(destino As String)
        If _bytesNif Is Nothing OrElse _bytesNif.Length = 0 Then Return

        Dim r As Exportador.Resultado
        Using New EsperaVisual(Me)
            r = Exportador.Exportar(destino, _relNif, _bytesNif, _dependencias, _resolutor)
        End Using

        Dim msg = $"Exported {r.Escritos.Count} files to {destino}"
        If r.Fallados.Count > 0 Then msg &= $" · {r.Fallados.Count} failed"
        MostrarEstado(msg, If(r.Fallados.Count > 0, Tema.ColAlerta, Tema.ColAcento))
        If r.Fallados.Count > 0 Then
            tips.SetToolTip(lblEstado, "Not exported:" & vbCrLf & String.Join(vbCrLf, r.Fallados.Take(20)))
        Else
            tips.SetToolTip(lblEstado, String.Join(vbCrLf, r.Escritos.Take(20)))
        End If
    End Sub

    ' ══════════════════════════════════ arrastrar y soltar ══════════════════════════════════

    Private Sub MainForm_DragEnter(sender As Object, e As DragEventArgs) Handles MyBase.DragEnter
        If e.Data IsNot Nothing AndAlso e.Data.GetDataPresent(DataFormats.FileDrop) Then
            e.Effect = DragDropEffects.Copy
        Else
            e.Effect = DragDropEffects.None
        End If
    End Sub

    ''' <summary>Soltar carpetas o BA2/BSA los agrega como fuentes del juego activo. Soltar un .nif lo
    ''' abre directamente (su carpeta NO se agrega sola: las fuentes las elige el usuario).</summary>
    Private Sub MainForm_DragDrop(sender As Object, e As DragEventArgs) Handles MyBase.DragDrop
        Dim datos = TryCast(e.Data?.GetData(DataFormats.FileDrop), String())
        If datos Is Nothing OrElse datos.Length = 0 Then Return

        Dim nuevas As New List(Of String)
        Dim nif As String = Nothing
        For Each p In datos
            If Directory.Exists(p) Then
                nuevas.Add(p)
            ElseIf String.Equals(Path.GetExtension(p), ".nif", StringComparison.OrdinalIgnoreCase) Then
                If nif Is Nothing Then nif = p
            ElseIf String.Equals(Path.GetExtension(p), ".ba2", StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(Path.GetExtension(p), ".bsa", StringComparison.OrdinalIgnoreCase) Then
                nuevas.Add(p)
            End If
        Next

        If nuevas.Count > 0 Then AgregarFuentes(nuevas)
        If nif IsNot Nothing Then
            Try
                Using New EsperaVisual(Me)
                    CargarDesdeBytes(File.ReadAllBytes(nif), Path.GetFileName(nif))
                End Using
            Catch ex As Exception
                MostrarEstado($"Could not open {Path.GetFileName(nif)}: {ex.Message}", Tema.ColRojo)
            End Try
        End If
    End Sub

    ''' <summary>Cursor de espera con alcance de bloque; sin esto los montajes de ~1 s parecen un cuelgue.</summary>
    Private NotInheritable Class EsperaVisual
        Implements IDisposable
        Private ReadOnly _f As Form
        Public Sub New(f As Form)
            _f = f
            _f.Cursor = Cursors.WaitCursor
            Application.DoEvents()
        End Sub
        Public Sub Dispose() Implements IDisposable.Dispose
            _f.Cursor = Cursors.Default
        End Sub
    End Class
End Class
