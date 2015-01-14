/* Main script for running fluid structure interaction (FSI) simulations by coupling the codes Star-CCM+ and Abaqus

   This file is used to create an executable file for running the following two scripts using the input file
		InputFile.txt - This text file has all the parameters/specifications that can be changed
		FSI_GeometryBuilder.py - This Python file creates the geometry/mesh for both the solid and fluid domains
		AbaqusMeshing.java - This Java file creates the Star-CCM+ file using the fluid mesh created in Abaqus
			NOTE: A Java jar file must also be referenced within Star-CCM+ this allows AbaqusMeshing.java to run

 	Written by Casey J. Jesse in June 2013 at the University of Missouri - Columbia
	Revisions:
		July 11, 2013 -  Plate mesh parameters were added to create biases in the plate's mesh 
		November 23, 2014 - Many undocumented updates have been completed since July 2013
*/
// Do not delete the following import lines
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

public class AbaqusStarCCM
{
    private static Hashtable m_doubleParameters = new Hashtable();
    private static Hashtable m_stringParameters = new Hashtable();
    private static Hashtable m_intParameters = new Hashtable();
    private static int m_i;
    private static string m_mode;

    private static string m_currentDirectory = Directory.GetCurrentDirectory();
    private static string m_inputFilePath = Path.Combine(m_currentDirectory, "InputFile.txt");
	private static string m_starOutputFile = Path.Combine(m_currentDirectory, "StarOutput.txt");

    static void Main()
    {

        readGeometryData(m_inputFilePath);
        m_mode = getStringData("CFDOrFSI");
        Console.Write("------------------------------------------------------------------------------------------------------------\n" +
            "\n The code is building/running fluid-structure interaction (FSI) models of parallel plate assemblies.\n\n");
        
        // Running the Abaqus Python script for building the plate and fluid models
        if (getDoubleData("smChHeight") != getDoubleData("lgChHeight") && getIntData("numOfPlates") > 1)
        {
            Console.WriteLine("\n Plate stack must have equal small and large channel heights! \n");
        }

        // Running the code in parametric mode
        else if (getStringData("parametricSwitch").Equals("true"))
        {
            string parameter2Change = getStringData("parameter2Change");
            double minValue = getDoubleData("minParameter");
            double maxValue = getDoubleData("maxParameter");
            double stepSize = getDoubleData("stepSize");

            // Creating a list of the variables to be changed in the study
            double value = minValue;
            List<double> values = new List<double>();
            while (value <= maxValue)
            {
                values.Add(value);
                value = value + stepSize;
            }

            //Looping through the parametric study
            Console.Write(" A parametric study has been started where the " + parameter2Change + 
                " will be varied from " + 
                minValue + " to " + maxValue + " in increments of " + stepSize + ".\n\n");
            for (m_i = 1; m_i <= values.Count; m_i++)
            {
                Console.WriteLine("\n Model " + m_i + " of " + values.Count + 
                    " in the parametric study is being built... \n");
                string newValue = System.Convert.ToString(values[m_i-1]);
                changeInputParameters(m_inputFilePath, parameter2Change, newValue);
                runAbaqusScript();
                runStarScript(values.Count);

                // Renaming the Star-CCM+ files
                string[] starFileExts = new string[] { ".sim", ".sim~" };
                for (int j = 0; j < starFileExts.Length; j++)
                {
                    string oldFileName = Path.Combine(m_currentDirectory, "Parametric_Study_" + m_mode +
                        "_Model_" + starFileExts[j]);
                    string newFileName = Path.Combine(m_currentDirectory, "Parametric_Study_" + m_mode +
                        "_Model_" + m_i + starFileExts[j]);
                    System.IO.File.Move(oldFileName, newFileName);
					if (getStringData("runStar").Equals("no"))
					{
						break;
					}
                }

                // Renaming the Abaqus files
                if (getStringData("runStar").Equals("yes") & m_mode.Equals("FSI"))
                {
                    renameAbaqusFiles(m_i);
				}
            }
        }

        // Running the code in serial mode
        else
        {
            // Running Abaqus to build the solid and fluid meshes as well as the solid model
            if (getStringData("createAbqInpFiles").Equals("yes"))
            {
                runAbaqusScript();
            }

            // Running Star-CCM+ to build the fluid model
            if (getStringData("createStarFile").Equals("yes"))
            {
                runStarScript(1);
            }
        }
    }

    /**
     * Method for reading data from an input file
     * 
     */
	public static void readGeometryData(string file2Read)
	{
        try
        {
            StreamReader file = new StreamReader(file2Read);
            string line;
            while((line = file.ReadLine()) != null)
            {
                if(line.StartsWith("#"))
                {
                    continue;
                }
                else if(line.Length == 0)
                {
                    continue;
                }
                else
                {
                    string[] things2Replace = new string[] {"\t", " "};
                    for (int i = 0; i <= 1; i++)
                    {
                        line = line.Replace(things2Replace[i], String.Empty);
                    }

                    string[] stringSeparators = new string[] { ":" };
                    string[] lineParameters = line.Split(stringSeparators, StringSplitOptions.RemoveEmptyEntries);

                    if(lineParameters[1].Equals("float"))
                    {
                        m_doubleParameters.Add(lineParameters[0], Double.Parse(lineParameters[2]));
                    }
                    else if(lineParameters[1].Equals("string"))
                    {
                        m_stringParameters.Add(lineParameters[0], lineParameters[2]);
                    }
                    else if(lineParameters[1].Equals("integer"))
                    {
                        m_intParameters.Add(lineParameters[0], int.Parse(lineParameters[2]));
                    }
                }
            }
            file.Close();
        }
        catch(FileNotFoundException e)
        {
            Console.WriteLine(e);
        }
	}

    /**
     * Getter methods for getting the double, string, and integer data
     */
    public static double getDoubleData(string variableName)
    {
        double variable = (double)m_doubleParameters[variableName];
        return variable;
    }

    public static string getStringData(string variableName)
    {
        string variable = (string)m_stringParameters[variableName];
        return variable;
    }

    public static int getIntData(string variableName)
    {
        int variable = (int)m_intParameters[variableName];
        return variable;
    }

    /*
     * This method runs the Abaqus python script to build the solid and fluid geometries
     */
    public static void runAbaqusScript()
    {
        // Running Abaqus to build the solid and fluid meshes as well as the solid model
        Console.WriteLine("\n\t Abaqus is building the solid model and the fluid geometry/mesh...\n");
        string buildAbaqusCall = "/C abaqus cae noGUI=AbaqusScript.py";
        var abaqusProcess = Process.Start("cmd.exe", buildAbaqusCall);
        abaqusProcess.WaitForExit();
        Console.WriteLine("\n\t Abaqus has finished building the solid model and the fluid geometry/mesh!\n");
    }

    /*
     * This method renames all of the Abaqus output files for use during a parametric study
     */
    public static void renameAbaqusFiles(int iter)
    {
        // Creating a list of the file extensions of the Abaqus' output files
        string[] fileExts = new string[] {".com", ".dat", ".log", ".msg", ".odb", ".prt", ".sim", ".sta",
            "CSE.log", "CSE_config.xml", "CSE_statechart.xml"};
        for (int i = 0; i < fileExts.Length; i++)
        {
            string oldFileName = Path.Combine(m_currentDirectory, "Parametric_Study_" + m_mode + 
                "_Model_Abaqus" + fileExts[i]);
            string newFileName = Path.Combine(m_currentDirectory, "Parametric_Study_" + m_mode + 
                "_Model_" + iter + "_Abaqus" + fileExts[i]);
            System.IO.File.Move(oldFileName, newFileName);
        }
    }

    /*
    * This method runs the StarCCM+ java script to build the solid and fluid geometries
    */
    public static void runStarScript(int numModels)
    {
        // Running Star-CCM+ to build the fluid model and setup the FSI problem
        string run = "";
        string end = "";
        string starProcesses = getStringData("starProcesses");
        if (getStringData("runStar").Equals("yes") & getStringData("parametricSwitch").Equals("true"))
        {
            run = "\n\t\t Model " + m_i + " of " + numModels + " will automatically start running after it has completed building... \n";
            end = "\n\t Model " + m_i + " of " + numModels + " has finished running! \n\n" +
                "\n\n------------------------------------------------------------------------------------------------------------";  
        }
        else if (getStringData("runStar").Equals("yes"))
        {
            run = "\n\t\t The " + m_mode + " model will automatically start running after it has completed building... \n";
            end = "\n\t The " + m_mode + " model has finished running! \n";
        }
        else
        {
            end = "\n\t Star-CCM+ has finished building the fluid model and the " + m_mode + " problem!\n";
        }
        Console.WriteLine("\n\t Star-CCM+ is now building the fluid model and setting up the " + m_mode + " problem...\n" + run);
        string buildStarCall = "/C starccm+ -new -np " + starProcesses + " -batch StarScript.java -batch-report";
		
		if (getStringData("createLogFile").Equals("yes"))
		{
			Process starP = new Process();
			starP.StartInfo.UseShellExecute = false;
			starP.StartInfo.RedirectStandardOutput = true;
			starP.StartInfo.FileName = "cmd.exe";
			starP.StartInfo.Arguments = buildStarCall;
			starP.Start();
			string output = starP.StandardOutput.ReadToEnd();
			starP.WaitForExit();
			
			// Exporting the log to a generic text file
			using (StreamWriter file = new StreamWriter(m_starOutputFile))
			{
				file.Write(output);
			}
        }
		else
		{
			var starProcess = Process.Start("cmd.exe", buildStarCall);
			starProcess.WaitForExit();
		}
		Console.WriteLine(end);
    }
    /*
     * This method runs the FSI simulation using Abaqus and StarCCM+ 
     */
    public static void runFSI()
    {
        // Running the FSI simulation
        string couplingScheme = getStringData("couplingScheme");
        string numStarProcesses = getStringData("starProcesses");
        double vel = getDoubleData("avgChVelocity");
        string plateGeometry = getStringData("plateGeometry");
        int intPlateThickness = (int)(getDoubleData("plateThickness") / 0.0254 * 1000);
        int intSmChHeight = (int)(getDoubleData("smChHeight") / 0.0254 * 1000);
        int intLgHeight = (int)(getDoubleData("lgChHeight") / 0.0254 * 1000);
        int numOfPlates = getIntData("numOfPlates");

        string runStarCall;
        if (getStringData("parametricSwitch").Equals("true"))
        {
            runStarCall = "/C starccm+ -np " + numStarProcesses + "-time -batch " + "Parametric_Study_" + m_mode + "_Model_" + m_i + ".sim";
        }
        else if (numOfPlates == 1)
        {
            runStarCall = "/C starccm+ -np " + numStarProcesses + "-time -batch " + m_mode + "_" +
                vel + "_" + plateGeometry + "_" + intPlateThickness + "_" + intSmChHeight + "_" +
                intLgHeight + ".sim";
        }
        else
        {
            runStarCall = "/C starccm+ -np " + numStarProcesses + "-time -batch " + m_mode + "_" +
                vel + "_" + plateGeometry + "_" + intPlateThickness + "_" + intSmChHeight + "_" +
                numOfPlates + "_PlateStack.sim";
        }
        var runStarProcess = Process.Start("cmd.exe", runStarCall);
        runStarProcess.WaitForExit();
    }

    /*
     * This method changes a variable in the input file
     */
    public static void changeInputParameters(string fileName, string parameter2Change, string newValue)
    {
        List<string> inputFileLines = new List<string>();
        using (StreamReader file = new StreamReader(fileName))
        {
            string line;
            while ((line = file.ReadLine()) != null)
            {
                if (line.StartsWith("#") || line.Length == 0)
                {
                    inputFileLines.Add(line);
                }
                else if (line.StartsWith(parameter2Change))
                {
                    string[] stringSeparators = new string[] { ":" };
                    string[] lineParameters = line.Split(stringSeparators, StringSplitOptions.RemoveEmptyEntries);
                    string newLine = 
                        lineParameters[0] + ":" + lineParameters[1] + ":\t\t" + newValue + ":" + lineParameters[3];
                    inputFileLines.Add(newLine);
                }
                else
                {
                    inputFileLines.Add(line);
                }
            }
            file.Close();
        }
        
        // Rewriting the input file
        using (StreamWriter file = new StreamWriter(fileName))
        {
            foreach (string line in inputFileLines)
            {
                file.Write(line +"\r\n");
            }
        }
    }
}