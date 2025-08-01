{
  description = "A Nix-flake-based EventStoreDB development environment";

  inputs.nixpkgs.url = "https://flakehub.com/f/NixOS/nixpkgs/0.1.*.tar.gz";

  outputs = { self, nixpkgs }:
    let
      supportedSystems =
        [ "x86_64-linux" "aarch64-linux" "x86_64-darwin" "aarch64-darwin" ];
      forEachSupportedSystem = f:
        nixpkgs.lib.genAttrs supportedSystems
        (system: f { pkgs = import nixpkgs { inherit system; }; });
    in {
      devShells = forEachSupportedSystem ({ pkgs }: {
        default = pkgs.mkShell rec {
          packages = with pkgs; [
            dotnetCorePackages.sdk_8_0
            dotnetCorePackages.aspnetcore_8_0
            glibcLocales
            bintools
            mono
          ];

          DOTNET_ROOT = "${pkgs.dotnetCorePackages.sdk_8_0}";
        };
      });
    };
}
