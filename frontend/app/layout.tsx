import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "storionX Migration Console",
  description: "Enterprise Vault to storionX migration demonstration",
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="tr">
      <body>{children}</body>
    </html>
  );
}
