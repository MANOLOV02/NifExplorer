Option Strict On

Imports System.Windows.Forms

''' <summary>Punto de entrada de Fast and High Quality Nif Explorer.</summary>
Module Program

    ''' <summary>Acepta un .nif suelto como argumento, para poder asociarlo a "Abrir con" (y para
    ''' verificar el camino completo de carga sin depender de la UI). Las fuentes siguen siendo las de
    ''' la lista: el NIF se abre, sus texturas se resuelven de donde el usuario haya dicho.</summary>
    <STAThread>
    Sub Main(args As String())
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
