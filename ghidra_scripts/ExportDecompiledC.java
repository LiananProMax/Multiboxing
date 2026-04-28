// Export all Ghidra decompiler output for the current program into one C-like file.
// Usage in headless mode:
//   -postScript ExportDecompiledC.java C:\path\to\output.c

import java.io.File;
import java.io.FileOutputStream;
import java.io.OutputStreamWriter;
import java.io.PrintWriter;
import java.nio.charset.StandardCharsets;

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileOptions;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.FunctionIterator;

public class ExportDecompiledC extends GhidraScript {
	@Override
	public void run() throws Exception {
		String[] args = getScriptArgs();
		if (args.length < 1) {
			throw new IllegalArgumentException("Missing output file path argument.");
		}

		File outputFile = new File(args[0]);
		File parent = outputFile.getParentFile();
		if (parent != null) {
			parent.mkdirs();
		}

		DecompInterface decompiler = new DecompInterface();
		decompiler.setOptions(new DecompileOptions());
		decompiler.toggleCCode(true);
		decompiler.toggleSyntaxTree(true);

		if (!decompiler.openProgram(currentProgram)) {
			throw new IllegalStateException("Unable to open program in decompiler.");
		}

		try (PrintWriter out = new PrintWriter(
			new OutputStreamWriter(new FileOutputStream(outputFile), StandardCharsets.UTF_8))) {
			out.printf("/* Decompiled with Ghidra from %s */%n", currentProgram.getName());
			out.printf("/* Image base: %s */%n%n", currentProgram.getImageBase());

			FunctionIterator functions = currentProgram.getFunctionManager().getFunctions(true);
			int count = 0;
			int failed = 0;

			for (Function function : functions) {
				monitor.checkCancelled();
				count++;

				out.printf("%n/* ============================================================%n");
				out.printf(" * Function: %s%n", function.getName());
				out.printf(" * Entry:    %s%n", function.getEntryPoint());
				out.printf(" * ============================================================ */%n");

				DecompileResults results = decompiler.decompileFunction(function, 90, monitor);
				if (results.decompileCompleted() && results.getDecompiledFunction() != null) {
					out.println(results.getDecompiledFunction().getC());
				} else {
					failed++;
					out.printf("/* Decompile failed: %s */%n", results.getErrorMessage());
				}
			}

			out.printf("%n/* Export complete. Functions: %d, failed: %d */%n", count, failed);
		} finally {
			decompiler.dispose();
		}
	}
}
