This library reads eXtreme Gammon .xg and .xgp files and write the contents to JSON files.
This C# solution has three projects: the conversion library, an xUnit project, and a console benchmark tool run by hand.
The corpus tests read the files in TestData\xg and/or TestData\xgp; they write no output there.
The binary layout was taken from https://www.extremegammon.com/XGformat.aspx - thanks to eXtreme Gammon for making this available!
