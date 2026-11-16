// The headless GUI tests reach the GUI's internal seams (the Results tab's run and time seams, GS2) directly instead of
// through reflection.
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("CampertronGUITests")]
