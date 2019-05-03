@ECHO OFF

REM ���R���p�C���I�v�V����
SET DEFINE=/define:NOCOMMENT
ECHO ���ҋ@�R�����g���\���ɂ��܂��B��

REM ���Q�Ƃ���DLL�ݒ�i�K�v�ɉ����ď��������Ă��������j
SET XDLL=LiveViewerBase.dll,Pack.dll,Microsoft.JScript.dll,System.dll,System.Data.dll,System.Drawing.dll,System.Windows.Forms.dll,System.Runtime.Remoting.dll,System.Security.dll,System.Xml.dll

REM ���R���p�C���̃p�X�ݒ�i�K�v�ɉ����ď��������Ă��������j
IF EXIST %WINDIR%\Microsoft.NET\Framework\v2.0.50727 PATH %PATH%;%WINDIR%\Microsoft.NET\Framework\v2.0.50727
IF EXIST %WINDIR%\Microsoft.NET\Framework\v3.0       PATH %PATH%;%WINDIR%\Microsoft.NET\Framework\v3.0
IF EXIST %WINDIR%\Microsoft.NET\Framework\v3.5       PATH %PATH%;%WINDIR%\Microsoft.NET\Framework\v3.5
IF EXIST %WINDIR%\Microsoft.NET\Framework\v4.0.30319 PATH %PATH%;%WINDIR%\Microsoft.NET\Framework\v4.0.30319

REM ���o�b�`�t�@�C���̒u���ꂽ�p�X�ֈړ�
CD /D "%~dp0"

REM ������������ΒP�ƃR���p�C���E������ΑS�R���p�C��
IF "%~1" EQU "" (
    GOTO :COMPILE_ALL
) ELSE (
    GOTO :COMPILE_SINGLE
)

REM ���P�ƃR���p�C��
:COMPILE_SINGLE
IF /I "%~x1" EQU ".cs" (
    CSC %DEFINE% /target:library /optimize+ /reference:%XDLL% "%~1"
) ELSE (
    IF /I "%~x1" EQU ".vb" (
        VBC /target:library /optimize+ /reference:%XDLL% "%~1"
    ) ELSE (
        ECHO [C#]��[VB]�����R���p�C���ł��܂��񁄁�
    )
)
GOTO :END

REM ���S�R���p�C��
:COMPILE_ALL
ECHO �S�R���p�C���J�n
FOR %%F IN (Plugin*.cs) DO (
    Echo [C#] %%F
    CSC %DEFINE% /target:library /optimize+ /nologo /reference:%XDLL% "%%F"
)
FOR %%F IN (Plugin*.vb) DO (
    Echo [VB] %%F
    VBC /target:library /optimize+ /nologo /reference:%XDLL% "%%F"
)
ECHO �S�R���p�C���I��
GOTO :END

REM ���I��
:END
Pause
