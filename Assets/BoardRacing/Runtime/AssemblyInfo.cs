using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("BoardRacing.Tests")]
[assembly: InternalsVisibleTo("BoardRacing.PlayModeTests")]
// The default editor assembly (Assets/Editor): capture harnesses build the
// racing surface directly.
[assembly: InternalsVisibleTo("Assembly-CSharp-Editor")]
