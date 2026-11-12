// Benign test-owned mock module: no vendor code, hooks, or DllMain.
extern "C" __declspec(dllexport) int SyntheticStreamlineAlias() { return 42; }
