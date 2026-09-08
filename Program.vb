Option Strict On

Imports System.Runtime.CompilerServices
Imports System.Windows.Forms

''' <summary>Punto de entrada de Fast and High Quality Nif Explorer.</summary>
Module Program

    ''' <summary>⛔ ACÁ NO SE TOCA NADA DE NINGÚN DLL PROPIO NI EL FORMULARIO PRINCIPAL, Y EL ARRANQUE REAL
    ''' VIVE EN <see cref="RealMain"/>. El JIT resuelve las referencias del cuerpo ENTERO de un método antes
    ''' de ejecutar su primera línea: con la creación de <c>MainForm</c> acá, una librería más vieja que la
    ''' que este exe pide mata el proceso ANTES de que el chequeo llegue a hablar. Es el mismo contrato que
    ''' <c>FO4_NPC_Manager\Program.vb</c> y el <c>MyApplication_Startup</c> de Wardrobe.
    ''' <para>Nif Explorer es el que MÁS superficie de mezcla tiene: se lleva tres DLL propios
    ''' (FO4_Base_Library, Ba2_Bsa_Library y NiflySharp), de dos repos que se versionan por separado.</para>
    ''' <para><c>NoInlining</c> en <c>RealMain</c> es PARTE DEL CONTRATO: inlineado, sus referencias vuelven a
    ''' resolverse en el JIT de Main y el agujero reaparece.</para></summary>
    <STAThread>
    Sub Main(args As String())
        If Not VersionGate.VerificarInstalacion() Then
            Environment.ExitCode = 1
            Return
        End If
        RealMain(args)
    End Sub

    ''' <summary>Acepta un .nif suelto como argumento, para poder asociarlo a "Abrir con" (y para
    ''' verificar el camino completo de carga sin depender de la UI). Las fuentes siguen siendo las de
    ''' la lista: el NIF se abre, sus texturas se resuelven de donde el usuario haya dicho.</summary>
    <MethodImpl(MethodImplOptions.NoInlining)>
    Private Sub RealMain(args As String())
        Application.SetHighDpiMode(HighDpiMode.SystemAware)
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)

        Dim nifInicial As String = Nothing
        Dim exportarA As String = Nothing
        If args IsNot Nothing Then
            For i = 0 To args.Length - 1
                Dim a = args(i)
                If a Is Nothing Then Continue For
                If a.EndsWith(".nif", StringComparison.OrdinalIgnoreCase) AndAlso IO.File.Exists(a) Then
                    If nifInicial Is Nothing Then nifInicial = a
                ElseIf String.Equals(a, "--export", StringComparison.OrdinalIgnoreCase) AndAlso i + 1 < args.Length Then
                    exportarA = args(i + 1)
                End If
            Next
        End If

        Application.Run(New MainForm(nifInicial, exportarA))
    End Sub
End Module
