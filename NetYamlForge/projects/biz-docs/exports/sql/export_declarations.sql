-- 輸出申告一覧
SELECT
    cd.DeclNo,
    cd.ExporterName,
    cd.ImporterName,
    cd.PortOfLoading,
    cd.PortOfDischarge,
    cd.DepartureDate,
    cd.ArrivalDate,
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
WHERE cd.DeclType = 'export'
ORDER BY cd.DepartureDate DESC, cd.DeclNo
