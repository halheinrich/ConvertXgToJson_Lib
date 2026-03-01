This library reads eXtreme Gammon .xg and .xgp files and write the contents to JSON files.
This C# solution has two projects; the conversion library and an xUnit project.
Place the input files into TestData\xg and/or TestData\xgp, run the RealFileTests, and the oputput is written to TestData\Output
The binary layout was taken from https://www.extremegammon.com/XGformat.aspx - thanks to eXtreme Gammon for making this available!
