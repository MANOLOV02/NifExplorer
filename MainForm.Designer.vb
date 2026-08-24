Option Strict On

Imports System.Drawing
Imports System.Windows.Forms

Partial Class MainForm
    Inherits Form

    Private components As System.ComponentModel.IContainer = Nothing

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing AndAlso components IsNot Nothing Then components.Dispose()
        MyBase.Dispose(disposing)
    End Sub

    ' ── cabecera ──
    Friend WithEvents lblTitulo As Label
    Friend WithEvents lblJuego As Label
    ' ── fuentes ──
    Friend WithEvents btnJuegoFO4 As Button
    Friend WithEvents btnJuegoSSE As Button
    Friend WithEvents btnAddArchive As Button
    Friend WithEvents btnAddCarpeta As Button
    Friend WithEvents btnSubir As Button
    Friend WithEvents btnBajar As Button
    Friend WithEvents btnQuitar As Button
    Friend WithEvents btnLimpiar As Button
    Friend WithEvents lstFuentes As ListBox
    ' ── árbol ──
    Friend WithEvents txtFiltro As TextBox
    Friend WithEvents treeNifs As TreeView
    ' ── pie ──
    Friend WithEvents btnLuces As Button
    Friend WithEvents btnExportar As Button
    Friend WithEvents lblEstado As Label
    ' ── contenedores ──
    Friend WithEvents split As SplitContainer
    Friend WithEvents splitIzq As SplitContainer
    Friend WithEvents panelPreview As Panel
    Friend WithEvents tips As ToolTip

    Private Sub InitializeComponent()
        components = New System.ComponentModel.Container()
        tips = New ToolTip(components)

        SuspendLayout()

        ' ────────────────────────────── ventana ──────────────────────────────
        Text = "Fast and High Quality Nif Explorer"
        ClientSize = New Size(1280, 800)
        MinimumSize = New Size(940, 600)
        StartPosition = FormStartPosition.CenterScreen
        WindowState = FormWindowState.Maximized
        BackColor = Tema.ColFondo
        ForeColor = Tema.ColFrente
        Font = Tema.Letra
        KeyPreview = True
        AllowDrop = True

        ' ────────────────────────────── cabecera ─────────────────────────────
        Dim cabecera As New Panel With {
            .Dock = DockStyle.Top, .Height = 40, .BackColor = Tema.ColSuperficie, .Padding = New Padding(14, 0, 14, 0)}

        lblTitulo = New Label With {
            .Text = "Fast and High Quality Nif Explorer",
            .Font = Tema.LetraTitulo, .ForeColor = Tema.ColFrente,
            .Dock = DockStyle.Left, .AutoSize = False, .Width = 420,
            .TextAlign = ContentAlignment.MiddleLeft}

        lblJuego = New Label With {
            .Text = "", .Font = Tema.Letra, .ForeColor = Tema.ColFrenteTenue,
            .Dock = DockStyle.Right, .AutoSize = False, .Width = 260,
            .TextAlign = ContentAlignment.MiddleRight}

        cabecera.Controls.Add(lblJuego)
        cabecera.Controls.Add(lblTitulo)

        ' ────────────────────────────── pie ──────────────────────────────────
        Dim pie As New Panel With {
            .Dock = DockStyle.Bottom, .Height = 46, .BackColor = Tema.ColSuperficie, .Padding = New Padding(12, 9, 12, 9)}

        ' UN SOLO botón para el diálogo de la librería: tiene las DOS pestañas (luces/sombras y render),
        ' así que "Lights" y "Render" abrían exactamente lo mismo.
        btnLuces = Tema.Boton("Lights && Render", 130)
        btnExportar = Tema.Boton("Export…", 100)

        Dim botonera As New FlowLayoutPanel With {
            .Dock = DockStyle.Left, .Width = 250, .FlowDirection = FlowDirection.LeftToRight,
            .WrapContents = False, .BackColor = Tema.ColSuperficie, .Margin = New Padding(0)}
        botonera.Controls.AddRange(New Control() {btnLuces, btnExportar})

        lblEstado = New Label With {
            .Dock = DockStyle.Fill, .ForeColor = Tema.ColFrenteTenue, .Font = Tema.Letra,
            .TextAlign = ContentAlignment.MiddleRight, .AutoEllipsis = True,
            .Text = "Add a folder or a BA2/BSA to start."}

        pie.Controls.Add(lblEstado)
        pie.Controls.Add(botonera)

        ' ────────────────────────────── split ────────────────────────────────
        ' ⛔ NI SplitterDistance NI los MinSize acá: mientras el control tiene su tamaño por defecto
        ' (150 px) cualquiera de los tres tira ArgumentOutOfRange. Se fijan en Load, ya con el layout hecho.
        split = New SplitContainer With {
            .Dock = DockStyle.Fill, .Orientation = Orientation.Vertical,
            .SplitterWidth = 4, .BackColor = Tema.ColBorde}
        split.Panel1.BackColor = Tema.ColFondo
        split.Panel2.BackColor = Tema.ColFondo

        ' ── columna izquierda: selector de juego + fuentes + filtro + árbol ──
        Dim izq As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 3,
            .BackColor = Tema.ColFondo, .Padding = New Padding(10, 10, 6, 10)}
        izq.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        izq.RowStyles.Add(New RowStyle(SizeType.Absolute, 30.0F))   ' juego
        izq.RowStyles.Add(New RowStyle(SizeType.Absolute, 32.0F))   ' barra fuentes
        izq.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))   ' fuentes | árbol, con divisor

        btnJuegoFO4 = Tema.Boton("Fallout 4", 100)
        btnJuegoSSE = Tema.Boton("Skyrim SE", 100)
        Dim filaJuego As New FlowLayoutPanel With {
            .Dock = DockStyle.Fill, .WrapContents = False, .BackColor = Tema.ColFondo, .Margin = New Padding(0)}
        filaJuego.Controls.AddRange(New Control() {btnJuegoFO4, btnJuegoSSE})

        btnAddArchive = Tema.Boton("+ BA2/BSA", 86)
        btnAddCarpeta = Tema.Boton("+ Folder", 74)
        btnSubir = Tema.Boton("▲", 32)
        btnBajar = Tema.Boton("▼", 32)
        btnQuitar = Tema.Boton("✕", 32)
        btnLimpiar = Tema.Boton("✕ All", 46)
        Dim filaBarra As New FlowLayoutPanel With {
            .Dock = DockStyle.Fill, .WrapContents = False, .BackColor = Tema.ColFondo, .Margin = New Padding(0)}
        filaBarra.Controls.AddRange(New Control() {btnAddArchive, btnAddCarpeta, btnSubir, btnBajar, btnQuitar, btnLimpiar})

        lstFuentes = New ListBox With {
            .Dock = DockStyle.Fill, .BackColor = Tema.ColSuperficie, .ForeColor = Tema.ColFrente,
            .BorderStyle = BorderStyle.None, .Font = Tema.Letra, .IntegralHeight = False,
            .DrawMode = DrawMode.OwnerDrawFixed, .ItemHeight = 20, .Margin = New Padding(0, 4, 0, 4)}

        txtFiltro = New TextBox With {
            .Dock = DockStyle.Fill, .BackColor = Tema.ColSuperficie, .ForeColor = Tema.ColFrente,
            .BorderStyle = BorderStyle.FixedSingle, .Font = Tema.Letra,
            .PlaceholderText = "Filter .nif by name…", .Margin = New Padding(0, 0, 0, 6)}

        treeNifs = New TreeView With {
            .Dock = DockStyle.Fill, .BackColor = Tema.ColSuperficie, .ForeColor = Tema.ColFrente,
            .BorderStyle = BorderStyle.None, .Font = Tema.Letra, .HideSelection = False,
            .ShowLines = False, .ShowRootLines = True, .FullRowSelect = True, .Indent = 16, .ItemHeight = 20}

        ' Divisor entre las fuentes (arriba) y el árbol (abajo). Igual que el de la ventana: ni
        ' SplitterDistance ni MinSize acá, que con el tamaño por defecto tiran ArgumentOutOfRange.
        splitIzq = New SplitContainer With {
            .Dock = DockStyle.Fill, .Orientation = Orientation.Horizontal,
            .SplitterWidth = 4, .BackColor = Tema.ColBorde, .Margin = New Padding(0, 4, 0, 0)}
        splitIzq.Panel1.BackColor = Tema.ColFondo
        splitIzq.Panel2.BackColor = Tema.ColFondo

        Dim abajo As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 2,
            .BackColor = Tema.ColFondo, .Padding = New Padding(0, 6, 0, 0)}
        abajo.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        abajo.RowStyles.Add(New RowStyle(SizeType.Absolute, 32.0F))
        abajo.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        abajo.Controls.Add(txtFiltro, 0, 0)
        abajo.Controls.Add(treeNifs, 0, 1)

        splitIzq.Panel1.Controls.Add(lstFuentes)
        splitIzq.Panel2.Controls.Add(abajo)

        izq.Controls.Add(filaJuego, 0, 0)
        izq.Controls.Add(filaBarra, 0, 1)
        izq.Controls.Add(splitIzq, 0, 2)

        ' ── columna derecha: el preview de la librería ──
        panelPreview = New Panel With {
            .Dock = DockStyle.Fill, .BackColor = Color.Black, .Padding = New Padding(0)}

        split.Panel1.Controls.Add(izq)
        split.Panel2.Controls.Add(panelPreview)

        Controls.Add(split)
        Controls.Add(pie)
        Controls.Add(cabecera)

        ResumeLayout(False)
    End Sub
End Class
