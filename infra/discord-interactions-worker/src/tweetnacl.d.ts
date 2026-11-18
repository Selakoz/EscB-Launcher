// Minimal ambient types for tweetnacl (the package ships no own typings).
declare module "tweetnacl" {
  const nacl: {
    sign: {
      detached: {
        verify(message: Uint8Array, signature: Uint8Array, publicKey: Uint8Array): boolean;
      };
    };
  };
  export default nacl;
}
