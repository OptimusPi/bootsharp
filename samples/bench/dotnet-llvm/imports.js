// Native imports for the `DllImport("x")` calls in Program.cs. The fixture data
// itself lives in ../fixtures.mjs and is wired onto globalThis by init.mjs (same
// pattern as the other samples); here we only marshal it across the wasm boundary
// using the Emscripten runtime helpers, which are unavailable to plain ES modules.

mergeInto(LibraryManager.library, {
    getNumber: () => globalThis.getNumber(),
    getStruct: () => {
        const json = JSON.stringify(globalThis.getStruct());
        const size = lengthBytesUTF16(json) + 1;
        const ptr = _malloc(size);
        stringToUTF16(json, ptr, size);
        return ptr; // has to be freed after use in real use cases
    }
});
