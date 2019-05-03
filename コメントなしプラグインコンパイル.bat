@ECHO OFF

REM ■コンパイルオプション
SET DEFINE=/define:NOCOMMENT
ECHO ＊待機コメントを非表示にします。＊

REM ■参照するDLL設定（必要に応じて書き換えてください）
SET XDLL=LiveViewerBase.dll,Pack.dll,Microsoft.JScript.dll,System.dll,System.Data.dll,System.Drawing.dll,System.Windows.Forms.dll,System.Runtime.Remoting.dll,System.Security.dll,System.Xml.dll

REM ■コンパイラのパス設定（必要に応じて書き換えてください）
IF EXIST %WINDIR%\Microsoft.NET\Framework\v2.0.50727 PATH %PATH%;%WINDIR%\Microsoft.NET\Framework\v2.0.50727
IF EXIST %WINDIR%\Microsoft.NET\Framework\v3.0       PATH %PATH%;%WINDIR%\Microsoft.NET\Framework\v3.0
IF EXIST %WINDIR%\Microsoft.NET\Framework\v3.5       PATH %PATH%;%WINDIR%\Microsoft.NET\Framework\v3.5
IF EXIST %WINDIR%\Microsoft.NET\Framework\v4.0.30319 PATH %PATH%;%WINDIR%\Microsoft.NET\Framework\v4.0.30319

REM □バッチファイルの置かれたパスへ移動
CD /D "%~dp0"

REM □引数があれば単独コンパイル・無ければ全コンパイル
IF "%~1" EQU "" (
    GOTO :COMPILE_ALL
) ELSE (
    GOTO :COMPILE_SINGLE
)

REM □単独コンパイル
:COMPILE_SINGLE
IF /I "%~x1" EQU ".cs" (
    CSC %DEFINE% /target:library /optimize+ /reference:%XDLL% "%~1"
) ELSE (
    IF /I "%~x1" EQU ".vb" (
        VBC /target:library /optimize+ /reference:%XDLL% "%~1"
    ) ELSE (
        ECHO [C#]と[VB]しかコンパイルできません＞＜
    )
)
GOTO :END

REM □全コンパイル
:COMPILE_ALL
ECHO 全コンパイル開始
FOR %%F IN (Plugin*.cs) DO (
    Echo [C#] %%F
    CSC %DEFINE% /target:library /optimize+ /nologo /reference:%XDLL% "%%F"
)
FOR %%F IN (Plugin*.vb) DO (
    Echo [VB] %%F
    VBC /target:library /optimize+ /nologo /reference:%XDLL% "%%F"
)
ECHO 全コンパイル終了
GOTO :END

REM □終了
:END
Pause
