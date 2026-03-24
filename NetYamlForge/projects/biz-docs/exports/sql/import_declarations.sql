-- 輸入申告一覧
SELECT
    cd.DeclNo,
    cd.ImporterName,
    cd.ExporterName,
    cd.PortOfLoading,
    cd.PortOfDischarge,
    cd.ArrivalDate,
    cd.DepartureDate,
    cd.Incoterms,
    cd.Currency,
    ROUND(cd.TotalValue, 2)    AS TotalValue,
    cd.Packages,
    ROUND(cd.GrossWeightKg, 1) AS GrossWeightKg,
    ROUND(cd.NetWeightKg, 1)   AS NetWeightKg,
    cd.HsCode,
    cd.CargoDescription,
    cd.ContainerNo,
    cd.VesselName,
    cd.Status,
    inv.InvoiceNo
FROM CustomsDeclaration cd
LEFT JOIN Invoice inv ON inv.Id = cd.InvoiceId
WHERE cd.DeclType = 'import'
ORDER BY cd.ArrivalDate DESC, cd.DeclNo
