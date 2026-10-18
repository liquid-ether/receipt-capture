#!/usr/bin/env python3
"""Generates AppResources.resx (en) and AppResources.fr.resx from one table,
so a key can never exist in one language and be missing from the other."""

import io
import os
import xml.sax.saxutils as sx

# key: (english, french, comment)
STRINGS = [
    # --- shell / navigation -------------------------------------------------
    ("App_Title", "Receipt Capture", "Capture de reçus", "Application name"),
    ("Tab_Capture", "Capture", "Capture", None),
    ("Tab_History", "History", "Historique", None),
    ("Tab_Settings", "Settings", "Réglages", None),

    # --- capture ------------------------------------------------------------
    ("Capture_Title", "Capture a receipt", "Capturer un reçu", None),
    ("Capture_TakePhoto", "Take a photo", "Prendre une photo", None),
    ("Capture_ChooseFromLibrary", "Choose from library", "Choisir dans la photothèque", None),
    ("Capture_Reading", "Reading the receipt…", "Lecture du reçu…", None),
    ("Capture_CameraUnavailable", "The camera is not available on this device.",
     "L'appareil photo n'est pas disponible sur cet appareil.", None),
    ("Capture_NothingRecognized", "No text could be read from that photo. Try again with more light.",
     "Aucun texte n'a pu être lu sur cette photo. Réessayez avec plus de lumière.", None),
    ("Capture_PendingCount", "{0} receipt(s) waiting to be sent", "{0} reçu(s) en attente d'envoi",
     "{0} is a count"),

    # --- review -------------------------------------------------------------
    ("Review_Title", "Check the details", "Vérifier les détails", None),
    ("Review_Merchant", "Merchant", "Commerçant", None),
    ("Review_Date", "Date", "Date", None),
    ("Review_Currency", "Currency", "Devise", None),
    ("Review_Subtotal", "Subtotal", "Sous-total", None),
    ("Review_Taxes", "Taxes", "Taxes", None),
    ("Review_Tip", "Tip", "Pourboire", None),
    ("Review_Total", "Total", "Total", None),
    ("Review_Category", "Category", "Catégorie", None),
    ("Review_Notes", "Notes", "Notes", None),
    ("Review_AddTax", "Add a tax line", "Ajouter une ligne de taxe", None),
    ("Review_RemoveTax", "Remove", "Retirer", None),
    ("Review_TaxCode", "Code", "Code", None),
    ("Review_TaxAmount", "Amount", "Montant", None),
    ("Review_Send", "Send to accounting", "Envoyer à la comptabilité", None),
    ("Review_Discard", "Discard", "Supprimer", None),
    ("Review_DiscardConfirm", "Discard this receipt and its photo?",
     "Supprimer ce reçu et sa photo ?", None),
    ("Review_Queued", "Receipt queued for the accounting system.",
     "Reçu mis en file d'attente pour la comptabilité.", None),

    # --- parse warnings (keys come from ParseWarnings) -----------------------
    ("Warning_MerchantNotFound", "The merchant name could not be read. Please enter it.",
     "Le nom du commerçant n'a pas pu être lu. Veuillez le saisir.", None),
    ("Warning_DateNotFound", "No date was found on the receipt. Please enter it.",
     "Aucune date n'a été trouvée sur le reçu. Veuillez la saisir.", None),
    ("Warning_DateAmbiguous", "The date could be read two ways. Please confirm it.",
     "La date peut se lire de deux façons. Veuillez la confirmer.", None),
    ("Warning_TotalNotFound", "The total could not be read. Please enter it.",
     "Le total n'a pas pu être lu. Veuillez le saisir.", None),
    ("Warning_TotalComputed", "The total was calculated from the subtotal and taxes. Please confirm it.",
     "Le total a été calculé à partir du sous-total et des taxes. Veuillez le confirmer.", None),
    ("Warning_TotalsDoNotReconcile", "Subtotal, taxes and total do not add up. Please check the amounts.",
     "Le sous-total, les taxes et le total ne concordent pas. Veuillez vérifier les montants.", None),

    # --- status -------------------------------------------------------------
    ("Status_Draft", "Draft", "Brouillon", None),
    ("Status_Pending", "Waiting to send", "En attente d'envoi", None),
    ("Status_Sent", "Sent", "Envoyé", None),
    ("Status_Failed", "Failed", "Échec", None),

    # --- history ------------------------------------------------------------
    ("History_Title", "History", "Historique", None),
    ("History_Empty", "No receipts yet. Capture one to get started.",
     "Aucun reçu pour l'instant. Capturez-en un pour commencer.", None),
    ("History_SyncNow", "Send pending receipts", "Envoyer les reçus en attente", None),
    ("History_Retry", "Try again", "Réessayer", None),
    ("History_Delete", "Delete", "Supprimer", None),
    ("History_SyncResult", "{0} sent, {1} to retry, {2} failed",
     "{0} envoyé(s), {1} à réessayer, {2} en échec", "{0},{1},{2} are counts"),

    # --- settings -----------------------------------------------------------
    ("Settings_Title", "Settings", "Réglages", None),
    ("Settings_AccountingSection", "Accounting system", "Système comptable", None),
    ("Settings_Endpoint", "Endpoint URL", "URL du point d'accès", None),
    ("Settings_EndpointPlaceholder", "https://books.example.com/api/receipts",
     "https://books.example.com/api/receipts", None),
    ("Settings_ApiKey", "API key", "Clé d'API", None),
    ("Settings_ApiKeyHeader", "API key header", "En-tête de la clé d'API", None),
    ("Settings_IncludeImage", "Send the receipt photo", "Envoyer la photo du reçu", None),
    ("Settings_TestConnection", "Test connection", "Tester la connexion", None),
    ("Settings_ConnectionOk", "Connection succeeded.", "Connexion réussie.", None),
    ("Settings_ConnectionFailed", "Connection failed: {0}", "Échec de la connexion : {0}",
     "{0} is the error detail"),
    ("Settings_Save", "Save", "Enregistrer", None),
    ("Settings_Saved", "Settings saved.", "Réglages enregistrés.", None),
    ("Settings_Language", "Language", "Langue", None),
    ("Settings_ApiKeyNote",
     "The API key is set on the server and is never sent to this browser.",
     "La clé d'API est définie sur le serveur et n'est jamais transmise à ce navigateur.", None),
    ("Settings_ApiKeyConfigured", "An API key is configured.", "Une clé d'API est configurée.", None),
    ("Settings_ApiKeyMissing",
     "No API key is configured. Set ReceiptCapture__Accounting__ApiKey on the server.",
     "Aucune clé d'API n'est configurée. Définissez ReceiptCapture__Accounting__ApiKey sur le serveur.", None),
    ("Settings_HealthEndpoint", "Test URL (optional)", "URL de test (facultatif)", None),

    # --- camera -------------------------------------------------------------
    ("Camera_Explain",
     "To photograph a receipt, this app needs permission to use your camera. "
     "Your browser will ask you to allow it.",
     "Pour photographier un reçu, cette application a besoin de l'autorisation d'utiliser votre appareil photo. "
     "Votre navigateur vous demandera de l'autoriser.", None),
    ("Camera_Enable", "Use the camera", "Utiliser l'appareil photo", None),
    ("Camera_Capture", "Take the photo", "Prendre la photo", None),
    ("Camera_Cancel", "Close the camera", "Fermer l'appareil photo", None),
    ("Camera_PermissionDenied",
     "Camera access was refused. Allow it in your browser settings, or choose a photo instead.",
     "L'accès à l'appareil photo a été refusé. Autorisez-le dans les réglages de votre navigateur, "
     "ou choisissez plutôt une photo.", None),
    ("Camera_Unavailable",
     "No camera is available on this device. You can choose a photo instead.",
     "Aucun appareil photo n'est disponible sur cet appareil. Vous pouvez plutôt choisir une photo.", None),
    ("Camera_InsecureContext",
     "Browsers only allow camera access over HTTPS. Open this app with an https:// address.",
     "Les navigateurs n'autorisent l'accès à l'appareil photo qu'en HTTPS. "
     "Ouvrez cette application avec une adresse https://.", None),
    ("Capture_ChooseFile", "Choose a photo instead", "Choisir une photo à la place", None),
    ("Capture_Uploading", "Sending the photo…", "Envoi de la photo…", None),

    # --- access gate --------------------------------------------------------
    ("Session_Title", "Enter the access code", "Saisissez le code d'accès", None),
    ("Session_CodeLabel", "Access code", "Code d'accès", None),
    ("Session_Submit", "Continue", "Continuer", None),
    ("Session_Invalid", "That code was not accepted.", "Ce code n'a pas été accepté.", None),
    ("Session_TooManyAttempts",
     "Too many attempts. Wait a few minutes and try again.",
     "Trop de tentatives. Patientez quelques minutes avant de réessayer.", None),
    ("Session_SignOut", "Sign out", "Se déconnecter", None),

    # --- misc web -----------------------------------------------------------
    ("Common_Retry", "Retry", "Réessayer", None),
    ("Common_Close", "Close", "Fermer", None),
    ("Error_Network",
     "Could not reach the server. Check your connection and try again.",
     "Impossible de joindre le serveur. Vérifiez votre connexion et réessayez.", None),

    # --- validation ---------------------------------------------------------
    ("Validation_TotalRequired", "Enter the total shown on the receipt.",
     "Saisissez le total indiqué sur le reçu.", None),
    ("Validation_TotalPositive", "The total must be greater than zero.",
     "Le total doit être supérieur à zéro.", None),
    ("Validation_EndpointRequired", "Enter the endpoint URL of your accounting system.",
     "Saisissez l'URL du point d'accès de votre système comptable.", None),
    ("Validation_EndpointHttps", "The endpoint URL must start with https://.",
     "L'URL du point d'accès doit commencer par https://.", None),

    # --- categories (codes from ExpenseCategories) --------------------------
    ("Category_MEALS", "Meals", "Repas", None),
    ("Category_TRAVEL", "Travel", "Déplacements", None),
    ("Category_LODGING", "Lodging", "Hébergement", None),
    ("Category_FUEL", "Fuel", "Carburant", None),
    ("Category_OFFICE_SUPPLIES", "Office supplies", "Fournitures de bureau", None),
    ("Category_SOFTWARE", "Software", "Logiciels", None),
    ("Category_TELECOM", "Telecommunications", "Télécommunications", None),
    ("Category_ENTERTAINMENT", "Entertainment", "Représentation", None),
    ("Category_PROFESSIONAL_SERVICES", "Professional services", "Services professionnels", None),
    ("Category_OTHER", "Other", "Autre", None),

    # --- common -------------------------------------------------------------
    ("Common_Ok", "OK", "OK", None),
    ("Common_Cancel", "Cancel", "Annuler", None),
    ("Common_Delete", "Delete", "Supprimer", None),
    ("Common_Error", "Something went wrong", "Une erreur est survenue", None),
]

HEADER = '''<?xml version="1.0" encoding="utf-8"?>
<root>
  <xsd:schema id="root" xmlns="" xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:msdata="urn:schemas-microsoft-com:xml-msdata">
    <xsd:import namespace="http://www.w3.org/XML/1998/namespace" />
    <xsd:element name="root" msdata:IsDataSet="true">
      <xsd:complexType>
        <xsd:choice maxOccurs="unbounded">
          <xsd:element name="metadata">
            <xsd:complexType>
              <xsd:sequence>
                <xsd:element name="value" type="xsd:string" minOccurs="0" />
              </xsd:sequence>
              <xsd:attribute name="name" use="required" type="xsd:string" />
              <xsd:attribute name="type" type="xsd:string" />
              <xsd:attribute name="mimetype" type="xsd:string" />
              <xsd:attribute ref="xml:space" />
            </xsd:complexType>
          </xsd:element>
          <xsd:element name="assembly">
            <xsd:complexType>
              <xsd:attribute name="alias" type="xsd:string" />
              <xsd:attribute name="name" type="xsd:string" />
            </xsd:complexType>
          </xsd:element>
          <xsd:element name="data">
            <xsd:complexType>
              <xsd:sequence>
                <xsd:element name="value" type="xsd:string" minOccurs="0" msdata:Ordinal="1" />
                <xsd:element name="comment" type="xsd:string" minOccurs="0" msdata:Ordinal="2" />
              </xsd:sequence>
              <xsd:attribute name="name" type="xsd:string" use="required" msdata:Ordinal="1" />
              <xsd:attribute name="type" type="xsd:string" msdata:Ordinal="3" />
              <xsd:attribute name="mimetype" type="xsd:string" msdata:Ordinal="4" />
              <xsd:attribute ref="xml:space" />
            </xsd:complexType>
          </xsd:element>
          <xsd:element name="resheader">
            <xsd:complexType>
              <xsd:sequence>
                <xsd:element name="value" type="xsd:string" minOccurs="0" msdata:Ordinal="1" />
              </xsd:sequence>
              <xsd:attribute name="name" type="xsd:string" use="required" />
            </xsd:complexType>
          </xsd:element>
        </xsd:choice>
      </xsd:complexType>
    </xsd:element>
  </xsd:schema>
  <resheader name="resmimetype">
    <value>text/microsoft-resx</value>
  </resheader>
  <resheader name="version">
    <value>2.0</value>
  </resheader>
  <resheader name="reader">
    <value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
  </resheader>
  <resheader name="writer">
    <value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
  </resheader>
'''


def write_resx(path, index, include_comments):
    parts = [HEADER]
    for entry in STRINGS:
        key, comment = entry[0], entry[3]
        value = entry[index]
        parts.append('  <data name="%s" xml:space="preserve">\n' % sx.escape(key))
        parts.append('    <value>%s</value>\n' % sx.escape(value))
        if include_comments and comment:
            parts.append('    <comment>%s</comment>\n' % sx.escape(comment))
        parts.append('  </data>\n')
    parts.append('</root>\n')

    os.makedirs(os.path.dirname(path), exist_ok=True)
    with io.open(path, 'w', encoding='utf-8') as handle:
        handle.write(''.join(parts))
    print('wrote %s (%d strings)' % (path, len(STRINGS)))


if __name__ == '__main__':
    root = '/home/user/receipt-capture/src/ReceiptCapture.Core/Resources/Strings'
    write_resx(os.path.join(root, 'AppResources.resx'), 1, True)
    write_resx(os.path.join(root, 'AppResources.fr.resx'), 2, False)
